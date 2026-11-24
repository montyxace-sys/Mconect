using System.Reflection;
using Mconect.Core.Protocol;
using Mconect.Core.Remoting;

namespace Mconect.Cli;

/// <summary>Utilidades de Mconect para terminal en cualquier plataforma.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Help();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "help" or "--help" or "-h" => Help(),
                "version" or "--version" => Version(),
                "invite" => Invite(args),
                "parse" => Parse(args),
                "connect" => Connect(args),
                _ => Unknown(args[0])
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException
                                       or System.Net.Sockets.SocketException or ArgumentException
                                       or OperationCanceledException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int Help()
    {
        Console.WriteLine("Mconect 1.1.0 — utilidades de conexión (visor portable por terminal)");
        Console.WriteLine();
        Console.WriteLine("  mconect version");
        Console.WriteLine("  mconect invite <host[:puerto]> <contraseña>");
        Console.WriteLine("      Muestra el texto de invitación listo para compartir.");
        Console.WriteLine("  mconect parse <invitación|host[:puerto]>");
        Console.WriteLine("      Muestra el ID, el puerto y la contraseña de una invitación.");
        Console.WriteLine("  mconect connect <invitación|host[:puerto]> [contraseña] [--frames N] [--outDIR]");
        Console.WriteLine("      Se conecta, guarda N fotogramas JPEG y cierra la sesión.");
        Console.WriteLine();
        Console.WriteLine("Permitir conexiones requiere la edición Windows de Mconect.");
        return 0;
    }

    private static int Version()
    {
        Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.0");
        return 0;
    }

    private static int Invite(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Uso: mconect invite <host[:puerto]> <contraseña>");
            return 2;
        }

        if (!ConnectionTarget.TryParse(args[1], out var target))
        {
            Console.Error.WriteLine("ID no válido. Ejemplo: 192.168.1.20:52222");
            return 2;
        }

        Console.WriteLine(ConnectionInvite.Create(target.Host, target.Port, args[2]).ToShareText());
        return 0;
    }

    private static int Parse(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Uso: mconect parse <invitación|host[:puerto]>");
            return 2;
        }

        if (!ConnectionInvite.TryParse(args[1], out var invite))
        {
            Console.Error.WriteLine("No se reconoce esa invitación.");
            return 2;
        }

        Console.WriteLine($"ID:         {invite.Target.Host}");
        Console.WriteLine($"Puerto:     {invite.Target.Port}");
        Console.WriteLine($"Contraseña: {(invite.Password.Length == 0 ? "(no incluida)" : invite.Password)}");
        return 0;
    }

    private static int Connect(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Uso: mconect connect <invitación|host[:puerto]> [contraseña] [--frames N] [--out DIR]");
            return 2;
        }

        if (!ConnectionInvite.TryParse(args[1], out var invite))
        {
            Console.Error.WriteLine("No se reconoce esa invitación.");
            return 2;
        }

        var password = args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal)
            ? args[2]
            : invite.Password;
        if (password.Length == 0)
        {
            Console.Error.WriteLine("Falta la contraseña de la sesión.");
            return 2;
        }

        var frames = OptionInt(args, "--frames", 1);
        var output = Option(args, "--out") ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(output);

        var received = 0;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var connection = ClientConnection.ConnectAsync(
            invite.Target.Host, invite.Target.Port, password,
            screen =>
            {
                var path = Path.Combine(output, $"frame-{received + 1:000}.jpg");
                File.WriteAllBytes(path, screen.Jpeg);
                received++;
                Console.WriteLine($"Fotograma {received} guardado: {path}");
                if (received >= frames) done.TrySetResult(true);
            },
            reason => closed.TrySetResult(reason),
            cancellation.Token).GetAwaiter().GetResult();

        try
        {
            if (!Task.WhenAny(done.Task, closed.Task).GetAwaiter().GetResult().Equals(done.Task) ||
                !done.Task.IsCompleted)
            {
                Console.Error.WriteLine($"La sesión se cerró antes de recibir {frames} fotograma(s): {closed.Task.Result}");
                return 1;
            }

            Console.WriteLine($"Recibidos {received} fotograma(s) de {connection.RemoteAddress}.");
            return 0;
        }
        finally
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static string? Option(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }

        return null;
    }

    private static int OptionInt(string[] args, string name, int fallback)
    {
        var value = Option(args, name);
        return value is not null && int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Comando desconocido: {command}. Ejecuta «mconect help».");
        return 2;
    }
}
