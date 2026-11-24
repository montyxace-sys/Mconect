using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mconect.Core.Protocol;
using Mconect.Core.Remoting;

namespace Mconect.Portable.App;

/// <summary>
/// Visor portable de Mconect: recibe la pantalla del anfitrión y envía ratón y teclado.
/// Permitir conexiones sigue siendo una función exclusiva de la edición Windows.
/// </summary>
public partial class MainView : UserControl
{
    private ClientConnection? _connection;
    private int _screenWidth = 1;
    private int _screenHeight = 1;
    public MainView()
    {
        InitializeComponent();
        DisconnectButton.Click += (_, _) => _ = DisconnectAsync("Sesión finalizada por el visitante.");
        Unloaded += (_, _) => _ = DisconnectAsync("La ventana se ha cerrado.");
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        var text = InviteBox.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            StatusText.Text = "Escribe el ID del equipo o pega la invitación completa.";
            return;
        }

        ConnectionInvite invite;
        if (!ConnectionInvite.TryParse(text, out invite))
        {
            StatusText.Text = "No se reconoce ese ID o invitación.";
            return;
        }

        var password = PasswordBox.Text?.Trim() ?? string.Empty;
        if (password.Length == 0) password = invite.Password;
        if (password.Length == 0)
        {
            StatusText.Text = "Escribe la contraseña de la sesión.";
            return;
        }

        ConnectButton.IsEnabled = false;
        StatusText.Text = $"Conectando con {invite.Target}…";

        try
        {
            _connection = await ClientConnection.ConnectAsync(
                invite.Target.Host, invite.Target.Port, password,
                screen => Dispatcher.UIThread.Post(() => ShowScreen(screen)),
                reason => Dispatcher.UIThread.Post(() => OnClosed(reason)));
            StatusText.Text = $"Conectado a {_connection.RemoteAddress}";
            DisconnectButton.IsEnabled = true;
            ScreenHint.IsVisible = false;
            ScreenImage.Focus();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException
                                       or System.Net.Sockets.SocketException or OperationCanceledException
                                       or System.Security.Cryptography.CryptographicException)
        {
            StatusText.Text = ex is System.Net.Sockets.SocketException
                ? $"No se pudo conectar con {invite.Target}. Comprueba la IP, el puerto y la red."
                : ex.Message;
            _connection = null;
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private void ShowScreen(ScreenMessage screen)
    {
        if (_connection is null) return;
        _screenWidth = Math.Max(1, screen.ScreenWidth);
        _screenHeight = Math.Max(1, screen.ScreenHeight);
        try
        {
            using var stream = new MemoryStream(screen.Jpeg);
            ScreenImage.Source = new Bitmap(stream);
        }
        catch (ArgumentException)
        {
            // Fotograma a medio recibir: se descarta y se espera el siguiente.
        }
    }

    private void OnClosed(string reason)
    {
        _connection = null;
        ConnectButton.IsEnabled = true;
        DisconnectButton.IsEnabled = false;
        ScreenImage.Source = null;
        ScreenHint.IsVisible = true;
        StatusText.Text = reason;
    }

    private async Task DisconnectAsync(string reason)
    {
        var connection = _connection;
        if (connection is null) return;
        _connection = null;
        try
        {
            await connection.CloseAsync(reason);
        }
        catch (IOException)
        {
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    private (ushort X, ushort Y) Position(PointerEventArgs e)
    {
        var point = e.GetPosition(ScreenImage);
        var bounds = ScreenImage.Bounds;
        var x = bounds.Width <= 0 ? 0 : point.X / bounds.Width * _screenWidth;
        var y = bounds.Height <= 0 ? 0 : point.Y / bounds.Height * _screenHeight;
        return ((ushort)Math.Clamp(x, 0, ushort.MaxValue), (ushort)Math.Clamp(y, 0, ushort.MaxValue));
    }

    private void Screen_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_connection is null) return;
        var (x, y) = Position(e);
        _connection.SendMouse(new MouseMessage(MouseAction.Move, x, y, 0));
    }

    private void Screen_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_connection is null) return;
        ScreenImage.Focus();
        var (x, y) = Position(e);
        var action = e.GetCurrentPoint(ScreenImage).Properties.IsRightButtonPressed
            ? MouseAction.RightDown
            : MouseAction.LeftDown;
        _connection.SendMouse(new MouseMessage(action, x, y, 0));
    }

    private void Screen_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_connection is null) return;
        var (x, y) = Position(e);
        var action = e.InitialPressMouseButton == MouseButton.Right
            ? MouseAction.RightUp
            : MouseAction.LeftUp;
        _connection.SendMouse(new MouseMessage(action, x, y, 0));
    }

    private void Screen_PointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_connection is null) return;
        var (x, y) = Position(e);
        _connection.SendMouse(new MouseMessage(MouseAction.Wheel, x, y, (int)(e.Delta.Y * 120)));
    }

    private void Screen_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_connection is null) return;
        var key = VirtualKeys.FromAvalonia(e.Key);
        if (key > 0) _connection.SendKey(new KeyMessage(true, key));
    }

    private void Screen_KeyUp(object? sender, KeyEventArgs e)
    {
        if (_connection is null) return;
        var key = VirtualKeys.FromAvalonia(e.Key);
        if (key > 0) _connection.SendKey(new KeyMessage(false, key));
    }

    private void Donate_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var url = button.Tag?.ToString();
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            StatusText.Text = $"Abre este enlace para apoyar el proyecto: {url}";
        }
    }
}
