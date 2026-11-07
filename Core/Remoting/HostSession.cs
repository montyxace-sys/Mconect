using System.Diagnostics;
using System.Security.Cryptography;
using Mnet.Core.Protocol;

namespace Mnet.Core.Remoting;

/// <summary>Lado anfitrión de una sesión: saludo, consentimiento, envío de pantalla y entrada remota.</summary>
public static class HostSession
{
    public const int DefaultPort = 52222;

    private const int FrameIntervalMs = 80;
    private const int KeepAliveIntervalMs = 1_500;

    public static async Task RunAsync(
        Stream stream,
        string password,
        string remoteAddress,
        Func<string, bool> consent,
        IRemoteScreen screen,
        IInputSink input,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var handshake = new ServerHandshake();
        if (!handshake.Run(stream, password, out var error))
        {
            log?.Invoke($"Conexión rechazada: {error}");
            return;
        }

        // AesGcm no se comparte entre hilos: una instancia para enviar y otra para recibir.
        using var aesSend = new AesGcm(handshake.SessionKey, FrameCodec.TagSize);
        using var aesReceive = new AesGcm(handshake.SessionKey, FrameCodec.TagSize);

        if (!consent(remoteAddress))
        {
            WriteFrame(aesSend, stream, SessionMessages.EncodeConsent(false, "El anfitrión rechazó la conexión."));
            log?.Invoke("El anfitrión rechazó la conexión.");
            return;
        }

        WriteFrame(aesSend, stream, SessionMessages.EncodeConsent(true, string.Empty));
        log?.Invoke($"Sesión aceptada: {remoteAddress}");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var writeGate = new object();
        var send = Task.Run(() => SendLoopAsync(stream, aesSend, screen, writeGate, linked.Token, log));
        var receive = Task.Run(() => ReceiveLoopAsync(stream, aesReceive, aesSend, writeGate, input));

        await Task.WhenAny(send, receive).ConfigureAwait(false);
        linked.Cancel();
        WriteFrame(aesSend, stream, SessionMessages.EncodeBye("Sesión finalizada."), writeGate);

        // Cerrar el flujo desbloquea al hilo que estuviera esperando datos.
        try
        {
            stream.Dispose();
        }
        catch (IOException)
        {
        }

        try
        {
            await Task.WhenAll(send, receive).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidDataException or OperationCanceledException)
        {
        }

        log?.Invoke("Sesión terminada.");
    }

    private static async Task SendLoopAsync(
        Stream stream,
        AesGcm aes,
        IRemoteScreen screen,
        object writeGate,
        CancellationToken cancellationToken,
        Action<string>? log)
    {
        var stopwatch = Stopwatch.StartNew();
        byte[]? lastHash = null;
        var lastSent = long.MinValue;
        var captureErrors = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            ScreenFrame frame;
            try
            {
                frame = screen.Capture();
                captureErrors = 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (++captureErrors == 1)
                {
                    log?.Invoke($"No se pudo capturar la pantalla: {ex.Message}");
                }

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var hash = SHA256.HashData(frame.Jpeg);
            var now = stopwatch.ElapsedMilliseconds;
            if (lastHash is null || now - lastSent >= KeepAliveIntervalMs || !hash.AsSpan().SequenceEqual(lastHash))
            {
                var message = new ScreenMessage
                {
                    FrameWidth = frame.FrameWidth,
                    FrameHeight = frame.FrameHeight,
                    ScreenWidth = frame.ScreenWidth,
                    ScreenHeight = frame.ScreenHeight,
                    TimestampMs = now,
                    Jpeg = frame.Jpeg
                };

                WriteFrame(aes, stream, SessionMessages.EncodeScreen(message), writeGate);
                lastHash = hash;
                lastSent = now;
            }

            await Task.Delay(FrameIntervalMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ReceiveLoopAsync(Stream stream, AesGcm aes, AesGcm aesSend, object writeGate, IInputSink input)
    {
        while (true)
        {
            var payload = FrameCodec.Read(stream);
            if (payload is null)
            {
                return;
            }

            var opened = FrameCodec.TryOpen(aes, payload);
            if (opened is null || opened.Length == 0)
            {
                return;
            }

            switch ((MessageType)opened[0])
            {
                case MessageType.Mouse:
                    input.Mouse(SessionMessages.DecodeMouse(opened));
                    break;
                case MessageType.Key:
                    input.Key(SessionMessages.DecodeKey(opened));
                    break;
                case MessageType.Ping:
                    WriteFrame(aesSend, stream, SessionMessages.EncodePing(SessionMessages.DecodePing(opened)), writeGate);
                    break;
                case MessageType.Bye:
                    return;
            }
        }
    }

    private static void WriteFrame(AesGcm aes, Stream stream, byte[] payload, object? writeGate = null)
    {
        try
        {
            if (writeGate is null)
            {
                FrameCodec.Write(stream, FrameCodec.Seal(aes, payload));
                return;
            }

            lock (writeGate)
            {
                FrameCodec.Write(stream, FrameCodec.Seal(aes, payload));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }
}
