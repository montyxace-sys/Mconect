namespace Mconect.Core.Remoting;

/// <summary>
/// Invitación de conexión: dirección, puerto y contraseña de la sesión en un solo texto.
/// Formato: <c>mconect://192.168.1.20:52222?pwd=abcd2345</c>.
/// Es un enlace de uso manual: ningún equipo se conecta automáticamente al abrirlo.
/// </summary>
public readonly record struct ConnectionInvite(ConnectionTarget Target, string Password)
{
    public const string PasswordParameter = "pwd";

    public static ConnectionInvite Create(string host, int port, string password) =>
        new(new ConnectionTarget(host, port), password.Trim());

    /// <summary>Texto que se copia al portapapeles y se comparte por chat o correo.</summary>
    public string ToShareText()
    {
        var target = Target.ToString();
        var password = Uri.EscapeDataString(Password);
        return $"{ConnectionTarget.Scheme}{target}?{PasswordParameter}={password}";
    }

    public override string ToString() => ToShareText();

    /// <summary>Acepta el texto completo de la invitación o solo <c>host:puerto</c> sin contraseña.</summary>
    public static bool TryParse(string? text, out ConnectionInvite invite)
    {
        invite = default;
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0) return false;

        string password = string.Empty;
        var question = value.IndexOf('?');
        if (question >= 0)
        {
            var query = value[(question + 1)..];
            value = value[..question];
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = part.IndexOf('=');
                if (equals < 0) continue;
                if (!part[..equals].Equals(PasswordParameter, StringComparison.OrdinalIgnoreCase)) continue;
                password = Uri.UnescapeDataString(part[(equals + 1)..]).Trim();
                break;
            }
        }

        if (!ConnectionTarget.TryParse(value, out var target)) return false;
        invite = new ConnectionInvite(target, password);
        return true;
    }
}
