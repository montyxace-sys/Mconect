using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mconect.Core.Protocol;
using Mconect.Core.Remoting;
using MouseAction = Mconect.Core.Protocol.MouseAction;

namespace Mconect.App;

public partial class SessionWindow : Window
{
    private readonly DispatcherTimer _statsTimer;
    private ClientConnection? _connection;
    private ScreenMessage? _pendingFrame;
    private bool _renderPosted;
    private bool _fullScreen;
    private bool _closing;
    private int _framesThisSecond;
    private int _screenWidth;
    private int _screenHeight;

    public SessionWindow(string target)
    {
        InitializeComponent();

        Title = $"Mconect · {target}";
        RemoteNameText.Text = $"Conectado a {target}";

        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += (_, _) =>
        {
            StatsText.Text = _framesThisSecond > 0 ? $"{_framesThisSecond} fps" : string.Empty;
            _framesThisSecond = 0;
        };
        _statsTimer.Start();

        Loaded += (_, _) => RemoteImage.Focus();
    }

    public void Attach(ClientConnection connection)
    {
        _connection = connection;
        SessionStatusText.Text = "Sesión cifrada AES-256-GCM · control compartido";
        Activated += (_, _) => RemoteImage.Focus();
    }

    public void ShowFrame(ScreenMessage frame)
    {
        _screenWidth = frame.ScreenWidth;
        _screenHeight = frame.ScreenHeight;
        _pendingFrame = frame;

        if (_renderPosted)
        {
            return;
        }

        _renderPosted = true;
        Dispatcher.BeginInvoke(new Action(RenderPendingFrame), DispatcherPriority.Render);
    }

    private void RenderPendingFrame()
    {
        _renderPosted = false;
        var frame = _pendingFrame;
        if (frame is null || _closing)
        {
            return;
        }

        try
        {
            using var stream = new MemoryStream(frame.Jpeg);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            RemoteImage.Source = image;
            WaitingPanel.Visibility = Visibility.Collapsed;
            ScreenInfoText.Text = $"{frame.ScreenWidth} × {frame.ScreenHeight} · imagen {frame.FrameWidth} × {frame.FrameHeight}";
            _framesThisSecond++;
            _pendingFrame = null;
        }
        catch (NotSupportedException)
        {
        }
    }

    public void NotifyClosed(string reason)
    {
        if (_closing)
        {
            return;
        }

        SessionStatusText.Text = reason;
        StatsText.Text = string.Empty;
        MessageBox.Show(this, reason, "Sesión finalizada", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void SendMouse(MouseAction action, Point position, int wheel)
    {
        if (_connection is null || RemoteImage.Source is not BitmapSource source ||
            RemoteImage.ActualWidth <= 0 || RemoteImage.ActualHeight <= 0)
        {
            return;
        }

        var scale = Math.Min(RemoteImage.ActualWidth / source.PixelWidth,
            RemoteImage.ActualHeight / source.PixelHeight);
        var displayedWidth = source.PixelWidth * scale;
        var displayedHeight = source.PixelHeight * scale;
        var offsetX = (RemoteImage.ActualWidth - displayedWidth) / 2;
        var offsetY = (RemoteImage.ActualHeight - displayedHeight) / 2;

        var relativeX = (position.X - offsetX) / displayedWidth;
        var relativeY = (position.Y - offsetY) / displayedHeight;
        if (relativeX < 0 || relativeX > 1 || relativeY < 0 || relativeY > 1)
        {
            return;
        }

        var x = (ushort)Math.Clamp(relativeX * 65535, 0, 65535);
        var y = (ushort)Math.Clamp(relativeY * 65535, 0, 65535);
        _connection.SendMouse(new MouseMessage(action, x, y, wheel));
    }

    private void Image_MouseMove(object sender, MouseEventArgs e) =>
        SendMouse(MouseAction.Move, e.GetPosition(RemoteImage), 0);

    private void Image_MouseDown(object sender, MouseButtonEventArgs e)
    {
        RemoteImage.Focus();
        var action = e.ChangedButton switch
        {
            MouseButton.Left => MouseAction.LeftDown,
            MouseButton.Right => MouseAction.RightDown,
            MouseButton.Middle => MouseAction.MiddleDown,
            _ => MouseAction.Move
        };

        SendMouse(action, e.GetPosition(RemoteImage), 0);
    }

    private void Image_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var action = e.ChangedButton switch
        {
            MouseButton.Left => MouseAction.LeftUp,
            MouseButton.Right => MouseAction.RightUp,
            MouseButton.Middle => MouseAction.MiddleUp,
            _ => MouseAction.Move
        };

        SendMouse(action, e.GetPosition(RemoteImage), 0);
    }

    private void Image_MouseWheel(object sender, MouseWheelEventArgs e) =>
        SendMouse(MouseAction.Wheel, e.GetPosition(RemoteImage), e.Delta);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 || (e.Key == Key.Escape && _fullScreen))
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        if (_connection is null)
        {
            return;
        }

        var key = KeyInterop.VirtualKeyFromKey(e.Key);
        if (key == 0)
        {
            return;
        }

        _connection.SendKey(new KeyMessage(true, key));
        e.Handled = true;
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_connection is null || e.Key == Key.F11 || e.Key == Key.Escape)
        {
            return;
        }

        var key = KeyInterop.VirtualKeyFromKey(e.Key);
        if (key == 0)
        {
            return;
        }

        _connection.SendKey(new KeyMessage(false, key));
        e.Handled = true;
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private void ToggleFullScreen()
    {
        _fullScreen = !_fullScreen;
        if (_fullScreen)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            Toolbar.Visibility = Visibility.Collapsed;
            StatusBar.Visibility = Visibility.Collapsed;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
            Toolbar.Visibility = Visibility.Visible;
            StatusBar.Visibility = Visibility.Visible;
        }

        RemoteImage.Focus();
    }

    private void End_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        _closing = true;
        _statsTimer.Stop();

        var connection = _connection;
        _connection = null;
        if (connection is not null)
        {
            _ = connection.DisposeAsync().AsTask();
        }

        base.OnClosing(e);
    }
}
