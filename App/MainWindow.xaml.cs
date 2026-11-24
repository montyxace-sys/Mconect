using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Mconect.App.Services;
using Mconect.Core.Remoting;

namespace Mconect.App;

public partial class MainWindow : Window
{
    private const int DefaultPort = HostSession.DefaultPort;

    private string _password = MachineInfo.NewPassword();
    private HostService? _host;
    private SessionWindow? _session;
    private bool _waiting;

    public MainWindow()
    {
        InitializeComponent();

        LocalIdText.Text = MachineInfo.LocalAddress;
        LocalPortText.Text = $"Puerto {DefaultPort}";
        SessionPasswordText.Text = _password;
        HeaderAddress.Text = $"{MachineInfo.LocalAddress}:{DefaultPort}";
        VersionText.Text = $"Mconect {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";

        IdBox.KeyDown += (_, e) => SubmitOnEnter(e);
        SessionPasswordBox.KeyDown += (_, e) => SubmitOnEnter(e);
        Loaded += (_, _) =>
        {
            IdBox.Focus();
            if (DonationPrefs.ShouldShow())
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    new DonationDialog { Owner = this }.ShowDialog();
                };
                timer.Start();
            }
        };
    }

    private void SubmitOnEnter(KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        ConnectButton_Click(this, new RoutedEventArgs());
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        ConnectError.Visibility = Visibility.Collapsed;

        var target = IdBox.Text.Trim();
        if (target.Length == 0)
        {
            ShowConnectError("Escribe el ID del equipo al que quieres conectarte.");
            return;
        }

        if (SessionPasswordBox.Password.Length == 0)
        {
            ShowConnectError("Escribe la contraseña de la sesión.");
            return;
        }

        if (!ConnectionTarget.TryParse(target, out var connectionTarget))
        {
            ShowConnectError("El ID debe ser una IP, por ejemplo 192.168.1.20 o 192.168.1.20:52222.");
            return;
        }

        var host = connectionTarget.Host;
        var port = connectionTarget.Port;

        ConnectButton.IsEnabled = false;
        ConnectButton.Content = "Conectando…";

        var window = new SessionWindow($"{host}:{port}");
        try
        {
            var connection = await ClientConnection.ConnectAsync(
                host,
                port,
                SessionPasswordBox.Password,
                frame => Dispatcher.BeginInvoke(() => window.ShowFrame(frame)),
                reason => Dispatcher.BeginInvoke(() => window.NotifyClosed(reason)));

            window.Attach(connection);
            _session = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_session, window))
                {
                    _session = null;
                }
            };
            window.Show();
            window.Activate();
            HeaderStatus.Text = "Conectado";
        }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException or IOException
                                       or InvalidDataException or OperationCanceledException
                                       or System.Security.Cryptography.CryptographicException)
        {
            ShowConnectError(ex is SocketException
                ? $"No se pudo conectar con {host}:{port}. Comprueba el ID, la red y que el otro equipo esté esperando."
                : ex.Message);
            window.Close();
        }
        finally
        {
            ConnectButton.IsEnabled = true;
            ConnectButton.Content = "Conectar";
            HeaderStatus.Text = _waiting ? "Esperando conexiones" : "Listo";
        }
    }

    private void ShowConnectError(string message)
    {
        ConnectError.Text = message;
        ConnectError.Visibility = Visibility.Visible;
    }

    private void HostButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_waiting)
        {
            try
            {
                var host = new HostService(_password, RequestConsent);
                host.StatusChanged += message => Dispatcher.BeginInvoke(() =>
                {
                    HostStatus.Text = message;
                    HeaderStatus.Text = "Sesión remota activa";
                });
                host.Start(DefaultPort);

                _host = host;
                _waiting = true;
                HostButton.Content = "Dejar de permitir conexiones";
                HostButton.Style = (Style)FindResource("DangerButton");
                HostStatus.Text = "Esperando conexiones. Comparte tu ID y contraseña.";
                HeaderStatus.Text = _session is null ? "Esperando conexiones" : "Sesión remota activa";
            }
            catch (InvalidOperationException ex)
            {
                HostStatus.Text = ex.Message;
                _host?.Dispose();
                _host = null;
            }

            return;
        }

        _host?.Dispose();
        _host = null;
        _waiting = false;
        HostButton.Content = "Permitir conexiones";
        HostButton.Style = (Style)FindResource("SecondaryButton");
        HostStatus.Text = "El acceso remoto está desactivado.";
        HeaderStatus.Text = _session is null ? "Listo" : "Conectado";
    }

    private void NewPassword_Click(object sender, RoutedEventArgs e)
    {
        _password = MachineInfo.NewPassword();
        SessionPasswordText.Text = _password;

        if (_host is not null)
        {
            _host.Password = _password;
            HostStatus.Text = "Nueva contraseña lista para las próximas conexiones.";
        }
    }

    private void CopyId_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Clipboard.SetText($"{MachineInfo.LocalAddress}:{DefaultPort}");
            HostStatus.Text = "ID copiado al portapapeles.";
        }
        catch (ExternalException)
        {
            HostStatus.Text = "No se pudo copiar el ID; el portapapeles está ocupado.";
        }
    }

    private void CopyInvite_Click(object sender, RoutedEventArgs e)
    {
        var invite = ConnectionInvite.Create(MachineInfo.LocalAddress, DefaultPort, _password);
        try
        {
            Clipboard.SetText(invite.ToShareText());
            HostStatus.Text = "Invitación copiada: comparte el ID, el puerto y la contraseña.";
        }
        catch (ExternalException)
        {
            HostStatus.Text = "No se pudo copiar la invitación; el portapapeles está ocupado.";
        }
    }

    private void DonateLink_RequestNavigate(object sender,
        System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Clipboard.SetText(e.Uri.AbsoluteUri);
        }

        e.Handled = true;
    }

    private bool RequestConsent(string address)
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(() => RequestConsent(address));
        }

        var dialog = new ConsentDialog(address) { Owner = this };
        return dialog.ShowDialog() == true;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _host?.Dispose();
        _host = null;
        base.OnClosing(e);
    }
}
