using System.IO;
using System.Net;
using System.Net.Sockets;
using Mconect.Core.Remoting;

namespace Mconect.App.Services;

public sealed class HostService : IDisposable
{
    private readonly Func<string, bool> _consent;
    private readonly ScreenCapturer _screen = new();
    private readonly InputInjector _input = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private int _busy;

    public HostService(string password, Func<string, bool> consent)
    {
        Password = password;
        _consent = consent;
    }

    public string Password { get; set; }

    public event Action<string>? StatusChanged;

    public void Start(int port)
    {
        if (_listener is not null)
        {
            return;
        }

        var listener = new TcpListener(IPAddress.Any, port);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            listener.Stop();
            throw new InvalidOperationException($"No se pudo abrir el puerto {port}: {ex.Message}", ex);
        }

        _listener = listener;
        _cancellation = new CancellationTokenSource();
        _ = AcceptLoopAsync(listener, _cancellation.Token);
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                continue;
            }

            if (Interlocked.Exchange(ref _busy, 1) == 1)
            {
                client.Dispose();
                continue;
            }

            var address = client.Client.RemoteEndPoint is IPEndPoint endpoint
                ? endpoint.Address.ToString()
                : "un equipo desconocido";

            _ = Task.Run(async () =>
            {
                try
                {
                    await RunClientAsync(client, address, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Exchange(ref _busy, 0);
                }
            });
        }
    }

    private async Task RunClientAsync(TcpClient client, string address, CancellationToken cancellationToken)
    {
        using (client)
        {
            using var stream = client.GetStream();
            try
            {
                await HostSession.RunAsync(stream, Password, address, _consent, _screen, _input,
                    message => StatusChanged?.Invoke(message), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidDataException)
            {
                StatusChanged?.Invoke("La sesión terminó con un error: " + ex.Message);
            }
        }
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        try
        {
            _listener?.Stop();
        }
        catch (SocketException)
        {
        }

        _cancellation?.Dispose();
        _cancellation = null;
        _listener = null;
        StatusChanged?.Invoke("Acceso remoto desactivado.");
    }
}
