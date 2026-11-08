using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Mconect.Core.Protocol;
using Mconect.Core.Remoting;

namespace Mconect.CoreTests;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        FrameSealRoundTrip();
        TamperedFrameRejected();
        FrameStreamRoundTrip();
        TruncatedFrameRejected();
        OversizedFrameRejected();
        HandshakeOverLoopback();
        WrongPasswordRejected();
        GarbageHandshakeRejected();
        ScreenRoundTrip();
        MouseRoundTrip();
        KeyRoundTrip();
        PingRoundTrip();
        ByeRoundTrip();
        ConsentRoundTrip();
        HostClientEndToEnd();
        RejectedByHost();
        WrongMessageTypesRejected();

        Console.WriteLine();
        Console.WriteLine($"{_passed} correctas, {_failed} fallidas.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"OK   {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"FALLO {name}: {ex.Message}");
        }
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void FrameSealRoundTrip() => Check("AES-GCM: cifrar y descifrar", () =>
    {
        var key = RandomNumberGenerator.GetBytes(32);
        using var aes = new AesGcm(key, FrameCodec.TagSize);
        var plaintext = Encoding.UTF8.GetBytes("Mconect, sesión cifrada de extremo a extremo.");

        var frame = FrameCodec.Seal(aes, plaintext);
        Expect(frame.Length == FrameCodec.NonceSize + plaintext.Length + FrameCodec.TagSize,
            "Tamaño de trama inesperado.");

        var opened = FrameCodec.TryOpen(aes, frame);
        Expect(opened is not null && opened.AsSpan().SequenceEqual(plaintext),
            "El texto descifrado no coincide.");
    });

    private static void TamperedFrameRejected() => Check("AES-GCM: trama manipulada rechazada", () =>
    {
        var key = RandomNumberGenerator.GetBytes(32);
        using var aes = new AesGcm(key, FrameCodec.TagSize);
        var plaintext = RandomNumberGenerator.GetBytes(256);
        var frame = FrameCodec.Seal(aes, plaintext);

        frame[FrameCodec.NonceSize + 7] ^= 0x40;

        Expect(FrameCodec.TryOpen(aes, frame) is null, "Se aceptó una trama alterada.");
    });

    private static void FrameStreamRoundTrip() => Check("Marco: escribir y leer dos tramas", () =>
    {
        using var stream = new MemoryStream();
        var first = RandomNumberGenerator.GetBytes(1000);
        var second = RandomNumberGenerator.GetBytes(3);

        FrameCodec.Write(stream, first);
        FrameCodec.Write(stream, second);
        stream.Position = 0;

        Expect(FrameCodec.Read(stream)!.AsSpan().SequenceEqual(first), "Primera trama distinta.");
        Expect(FrameCodec.Read(stream)!.AsSpan().SequenceEqual(second), "Segunda trama distinta.");
        Expect(FrameCodec.Read(stream) is null, "Se esperaba fin de conexión.");
    });

    private static void TruncatedFrameRejected() => Check("Marco: trama truncada rechazada", () =>
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 0, 0, 0, 50 });
        stream.Write(new byte[] { 1, 2, 3, 4 });
        stream.Position = 0;

        try
        {
            FrameCodec.Read(stream);
            throw new InvalidOperationException("Se aceptó una trama truncada.");
        }
        catch (InvalidDataException)
        {
        }
    });

    private static void OversizedFrameRejected() => Check("Marco: tamaño fuera de rango rechazado", () =>
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF });
        stream.WriteByte(0x7F);
        stream.Position = 0;

        try
        {
            FrameCodec.Read(stream);
            throw new InvalidOperationException("Se aceptó una trama demasiado grande.");
        }
        catch (InvalidDataException)
        {
        }

        try
        {
            FrameCodec.Write(new MemoryStream(), new byte[FrameCodec.MaxFrameSize + 1]);
            throw new InvalidOperationException("Se permitió escribir una trama demasiado grande.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("demasiado grande"))
        {
        }
    });

    private static void HandshakeOverLoopback() => Check("Saludo: claves iguales por TCP local", () =>
    {
        const string password = "clave-de-prueba-2026";
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(() =>
        {
            using var client = listener.AcceptTcpClient();
            using var stream = client.GetStream();
            var server = new ServerHandshake();
            var ok = server.Run(stream, password, out var error);

            using var aes = new AesGcm(server.SessionKey, FrameCodec.TagSize);
            var probe = Encoding.UTF8.GetBytes("hola desde el servidor");
            FrameCodec.Write(stream, FrameCodec.Seal(aes, probe));

            var reply = FrameCodec.Read(stream);
            var opened = reply is null ? null : FrameCodec.TryOpen(aes, reply);
            return (ok, error, server.SessionKey, Probe: opened);
        });

        using var clientSocket = new TcpClient();
        clientSocket.Connect(IPAddress.Loopback, port);
        using var clientStream = clientSocket.GetStream();

        var client = new ClientHandshake();
        var accepted = client.Run(clientStream, password);
        Expect(accepted, $"El cliente rechazó el saludo: {client.Error}");

        using var clientAes = new AesGcm(client.SessionKey, FrameCodec.TagSize);
        var probe = FrameCodec.Read(clientStream);
        var opened = probe is null ? null : FrameCodec.TryOpen(clientAes, probe);
        Expect(opened is not null && Encoding.UTF8.GetString(opened) == "hola desde el servidor",
            "El cliente no pudo descifrar el sondeo del servidor.");

        FrameCodec.Write(clientStream, FrameCodec.Seal(clientAes,
            Encoding.UTF8.GetBytes("hola desde el cliente")));

        var (serverOk, serverError, serverKey, serverProbe) = serverTask.GetAwaiter().GetResult();
        listener.Stop();

        Expect(serverOk, $"El servidor rechazó el saludo: {serverError}");
        Expect(serverKey.AsSpan().SequenceEqual(client.SessionKey), "Las claves de sesión difieren.");
        Expect(serverProbe is not null && Encoding.UTF8.GetString(serverProbe) == "hola desde el cliente",
            "El servidor no pudo descifrar el sondeo del cliente.");
    });

    private static void WrongPasswordRejected() => Check("Saludo: contraseña incorrecta rechazada", () =>
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(() =>
        {
            using var client = listener.AcceptTcpClient();
            using var stream = client.GetStream();
            var server = new ServerHandshake();
            return (Ok: server.Run(stream, "correcta", out var error), Error: error);
        });

        using var clientSocket = new TcpClient();
        clientSocket.Connect(IPAddress.Loopback, port);
        using var clientStream = clientSocket.GetStream();

        var client = new ClientHandshake();
        var accepted = client.Run(clientStream, "equivocada");
        var (serverOk, serverError) = serverTask.GetAwaiter().GetResult();
        listener.Stop();

        Expect(!accepted, "El cliente aceptó una contraseña errónea.");
        Expect(!serverOk, "El servidor aceptó una contraseña errónea.");
        Expect(serverError == "Contraseña incorrecta.", $"Motivo inesperado: {serverError}");
        Expect(client.Error == "Contraseña incorrecta.", $"El cliente recibió: {client.Error}");
    });

    private static void GarbageHandshakeRejected() => Check("Saludo: datos basura rechazados", () =>
    {
        using var stream = new MemoryStream();
        FrameCodec.Write(stream, Encoding.ASCII.GetBytes("GET / HTTP/1.1"));
        stream.Position = 0;

        try
        {
            new ClientHandshake().Run(stream, "x");
            throw new InvalidOperationException("Se aceptó un saludo que no era de Mconect.");
        }
        catch (InvalidDataException)
        {
        }
    });

    private static void ScreenRoundTrip() => Check("Mensaje: pantalla", () =>
    {
        var jpeg = RandomNumberGenerator.GetBytes(4096);
        var message = new ScreenMessage
        {
            FrameWidth = 1280,
            FrameHeight = 720,
            ScreenWidth = 1920,
            ScreenHeight = 1080,
            TimestampMs = 1_760_000_000_123,
            Jpeg = jpeg
        };

        var decoded = SessionMessages.DecodeScreen(SessionMessages.EncodeScreen(message));

        Expect(decoded.FrameWidth == 1280 && decoded.FrameHeight == 720, "Dimensiones de trama distintas.");
        Expect(decoded.ScreenWidth == 1920 && decoded.ScreenHeight == 1080, "Dimensiones de pantalla distintas.");
        Expect(decoded.TimestampMs == message.TimestampMs, "Marca de tiempo distinta.");
        Expect(decoded.Jpeg.AsSpan().SequenceEqual(jpeg), "JPEG distinto.");
    });

    private static void MouseRoundTrip() => Check("Mensaje: ratón", () =>
    {
        var original = new MouseMessage(MouseAction.Wheel, 65535, 0, -120);
        var decoded = SessionMessages.DecodeMouse(SessionMessages.EncodeMouse(original));

        Expect(decoded.Action == MouseAction.Wheel, "Acción distinta.");
        Expect(decoded.X == 65535 && decoded.Y == 0, "Coordenadas distintas.");
        Expect(decoded.Wheel == -120, "Rueda distinta.");
    });

    private static void KeyRoundTrip() => Check("Mensaje: teclado", () =>
    {
        var decoded = SessionMessages.DecodeKey(
            SessionMessages.EncodeKey(new KeyMessage(true, 0x0D)));

        Expect(decoded.Down && decoded.VirtualKey == 0x0D, "Tecla distinta.");
    });

    private static void PingRoundTrip() => Check("Mensaje: latido", () =>
    {
        var ticks = DateTime.UtcNow.Ticks;
        Expect(SessionMessages.DecodePing(SessionMessages.EncodePing(ticks)) == ticks, "Latido distinto.");
    });

    private static void ByeRoundTrip() => Check("Mensaje: cierre con texto acentuado", () =>
    {
        var reason = "Sesión finalizada por el anfitrión · ñandú";
        Expect(SessionMessages.DecodeBye(SessionMessages.EncodeBye(reason)) == reason, "Motivo distinto.");
    });

    private static void ConsentRoundTrip() => Check("Mensaje: consentimiento", () =>
    {
        var (accepted, reason) = SessionMessages.DecodeConsent(
            SessionMessages.EncodeConsent(false, "No autorizado"));
        Expect(!accepted && reason == "No autorizado", "Rechazo distinto.");

        var yes = SessionMessages.DecodeConsent(SessionMessages.EncodeConsent(true, string.Empty));
        Expect(yes.Accepted && yes.Reason.Length == 0, "Aceptación distinta.");
    });

    private static void HostClientEndToEnd() => Check("Sesión: pantalla, ratón, teclado y cierre", () =>
    {
        const string password = "s3cret";
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var sink = new RecordingSink();
        var frames = 0;
        var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var hostTask = Task.Run(async () =>
        {
            using var tcp = await listener.AcceptTcpClientAsync();
            using var stream = tcp.GetStream();
            await HostSession.RunAsync(stream, password, "127.0.0.1",
                _ => true, new FakeScreen(), sink, null, CancellationToken.None);
        });

        ClientConnection? connection = null;
        try
        {
            connection = ClientConnection.ConnectAsync("127.0.0.1", port, password,
                _ =>
                {
                    if (Interlocked.Increment(ref frames) >= 3)
                    {
                        received.TrySetResult(true);
                    }
                },
                _ => closed.TrySetResult(true),
                CancellationToken.None).GetAwaiter().GetResult();

            Expect(received.Task.Wait(5000), "No llegaron fotogramas del anfitrión.");

            connection.SendMouse(new MouseMessage(MouseAction.LeftDown, 1234, 5678, 0));
            connection.SendKey(new KeyMessage(true, 0x41));

            Expect(WaitUntil(() => sink.MouseCount >= 1 && sink.KeyCount >= 1, 5000),
                "El anfitrión no recibió la entrada.");
            Expect(sink.LastMouse.Action == MouseAction.LeftDown &&
                   sink.LastMouse.X == 1234 && sink.LastMouse.Y == 5678, "Ratón distinto.");
            Expect(sink.LastKey.Down && sink.LastKey.VirtualKey == 0x41, "Tecla distinta.");

            connection.CloseAsync("Fin de la prueba").GetAwaiter().GetResult();
            Expect(closed.Task.Wait(5000), "El cliente no notificó el cierre.");
        }
        finally
        {
            if (connection is not null)
            {
                connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            listener.Stop();
        }

        Expect(hostTask.Wait(5000), "El anfitrión no terminó la sesión.");
    });

    private static void RejectedByHost() => Check("Sesión: rechazo del anfitrión", () =>
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var hostTask = Task.Run(async () =>
        {
            using var tcp = await listener.AcceptTcpClientAsync();
            using var stream = tcp.GetStream();
            await HostSession.RunAsync(stream, "clave", "127.0.0.1",
                _ => false, new FakeScreen(), new RecordingSink(), null, CancellationToken.None);
        });

        try
        {
            var error = Capture<InvalidOperationException>(() =>
                ClientConnection.ConnectAsync("127.0.0.1", port, "clave",
                    _ => { }, _ => { }, CancellationToken.None).GetAwaiter().GetResult());
            Expect(error.Message.Contains("rechazó"), $"Motivo inesperado: {error.Message}");
        }
        finally
        {
            listener.Stop();
            Expect(hostTask.Wait(5000), "El anfitrión no terminó tras rechazar.");
        }
    });

    private static T Capture<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T ex)
        {
            return ex;
        }

        throw new InvalidOperationException($"Se esperaba una excepción {typeof(T).Name}.");
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return condition();
    }

    private sealed class FakeScreen : IRemoteScreen
    {
        private int _frame;

        public ScreenFrame Capture()
        {
            var number = Interlocked.Increment(ref _frame);
            return new ScreenFrame(320, 180, 1920, 1080,
                new byte[] { 0xFF, 0xD8, (byte)number, (byte)(number >> 8), 0xFF, 0xD9 });
        }
    }

    private sealed class RecordingSink : IInputSink
    {
        private readonly object _gate = new();
        private int _mouseCount;
        private int _keyCount;
        private MouseMessage _lastMouse;
        private KeyMessage _lastKey;

        public int MouseCount { get { lock (_gate) { return _mouseCount; } } }
        public int KeyCount { get { lock (_gate) { return _keyCount; } } }
        public MouseMessage LastMouse { get { lock (_gate) { return _lastMouse; } } }
        public KeyMessage LastKey { get { lock (_gate) { return _lastKey; } } }

        public void Mouse(MouseMessage message)
        {
            lock (_gate)
            {
                _lastMouse = message;
                _mouseCount++;
            }
        }

        public void Key(KeyMessage message)
        {
            lock (_gate)
            {
                _lastKey = message;
                _keyCount++;
            }
        }
    }

    private static void WrongMessageTypesRejected() => Check("Mensaje: tipo cruzado rechazado", () =>
    {
        var mouse = SessionMessages.EncodeMouse(new MouseMessage(MouseAction.Move, 1, 2, 0));

        try
        {
            SessionMessages.DecodeKey(mouse);
            throw new InvalidOperationException("Se decodificó un ratón como teclado.");
        }
        catch (InvalidDataException)
        {
        }

        var screen = SessionMessages.EncodeScreen(new ScreenMessage { Jpeg = new byte[] { 1 } });
        try
        {
            SessionMessages.DecodeScreen(screen[..10]);
            throw new InvalidOperationException("Se aceptó una trama de pantalla corta.");
        }
        catch (InvalidDataException)
        {
        }
    });
}
