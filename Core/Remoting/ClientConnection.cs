using System.Net.Sockets;
using System.Security.Cryptography;
using Mnet.Core.Protocol;

namespace Mnet.Core.Remoting;

/// <summary>Lado visitante: conecta, recibe la pantalla y envía ratón y teclado por el canal cifrado.</summary>
public sealed class ClientConnection : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly AesGcm _aesSend;
    private readonly AesGcm _aesReceive;
    private readonly object _writeLock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _receiveTask;
    private int _closed;

    private ClientConnection(
        TcpClient client,
        NetworkStream stream,
        byte[] sessionKey,
        string remoteAddress,
        Action<ScreenMessage> onScreen,
        Action<string> onClosed)
    {
        _client = client;
        _stream = stream;
        _aesSend = new AesGcm(sessionKey, FrameCodec.TagSize);
        _aesReceive = new AesGcm(sessionKey, FrameCodec.TagSize);
        RemoteAddress = remoteAddress;
        _receiveTask = Task.Run(() => ReceiveLoop(onScreen, onClosed));
    }

    public string RemoteAddress { get; }

    public static async Task<ClientConnection> ConnectAsync(
        string host,
        int port,
        string password,
        Action<ScreenMessage> onScreen,
        Action<string> onClosed,
        CancellationToken cancellationToken = default)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();

            var handshake = new ClientHandshake();
            if (!handshake.Run(stream, password))
            {
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(handshake.Error) ? "No se pudo autenticar la sesión." : handshake.Error);
            }

            using (var aes = new AesGcm(handshake.SessionKey, FrameCodec.TagSize))
            {
                var payload = FrameCodec.Read(stream)
                    ?? throw new IOException("El anfitrión cerró la conexión antes de responder.");
                var opened = FrameCodec.TryOpen(aes, payload)
                    ?? throw new InvalidDataException("El anfitrión envió datos dañados.");
                var (accepted, reason) = SessionMessages.DecodeConsent(opened);
                if (!accepted)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrEmpty(reason) ? "El anfitrión rechazó la conexión." : reason);
                }
            }

            return new ClientConnection(client, stream, handshake.SessionKey,
                $"{host}:{port}", onScreen, onClosed);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public void SendMouse(MouseMessage message) => Send(SessionMessages.EncodeMouse(message));

    public void SendKey(KeyMessage message) => Send(SessionMessages.EncodeKey(message));

    public async Task CloseAsync(string reason = "Sesión finalizada.")
    {
        Send(SessionMessages.EncodeBye(reason));
        _cts.Cancel();
        try
        {
            _client.Close();
        }
        catch (SocketException)
        {
        }

        try
        {
            await _receiveTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidDataException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        await CloseAsync().ConfigureAwait(false);
        _aesSend.Dispose();
        _aesReceive.Dispose();
        _cts.Dispose();
        _stream.Dispose();
    }

    private void Send(byte[] payload)
    {
        try
        {
            lock (_writeLock)
            {
                FrameCodec.Write(_stream, FrameCodec.Seal(_aesSend, payload));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            _cts.Cancel();
        }
    }

    private void ReceiveLoop(Action<ScreenMessage> onScreen, Action<string> onClosed)
    {
        var reason = "La conexión se cerró.";

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var payload = FrameCodec.Read(_stream);
                if (payload is null)
                {
                    reason = "El anfitrión cerró la conexión.";
                    break;
                }

                var opened = FrameCodec.TryOpen(_aesReceive, payload);
                if (opened is null || opened.Length == 0)
                {
                    reason = "Se recibió un mensaje dañado; la sesión se ha cerrado.";
                    break;
                }

                switch ((MessageType)opened[0])
                {
                    case MessageType.Screen:
                        onScreen(SessionMessages.DecodeScreen(opened));
                        break;
                    case MessageType.Bye:
                        reason = SessionMessages.DecodeBye(opened);
                        break;
                    case MessageType.Ping:
                        break;
                }

                if ((MessageType)opened[0] == MessageType.Bye)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or SocketException)
        {
            reason = "La conexión se interrumpió.";
        }
        finally
        {
            onClosed(reason);
        }
    }
}
