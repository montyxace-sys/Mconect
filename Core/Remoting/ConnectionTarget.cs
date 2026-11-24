namespace Mconect.Core.Remoting;

/// <summary>Dirección de un equipo Mconect, con puerto explícito o el predeterminado.</summary>
public readonly record struct ConnectionTarget(string Host, int Port)
{
    public const string Scheme = "mconect://";

    public static ConnectionTarget Default(string host) => new(host, HostSession.DefaultPort);

    /// <summary>
    /// Acepta <c>192.168.1.20</c>, <c>192.168.1.20:52222</c>, <c>mconect://192.168.1.20:52222</c>
    /// y IPv6 entre corchetes (<c>[::1]:52222</c>).
    /// </summary>
    public static bool TryParse(string? text, out ConnectionTarget target)
    {
        target = default;
        var value = (text ?? string.Empty).Trim();
        if (value.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            value = value[Scheme.Length..].Trim();
        }

        if (value.Length == 0 || value.Any(char.IsWhiteSpace)) return false;

        string host;
        var port = HostSession.DefaultPort;

        if (value.StartsWith('['))
        {
            var close = value.IndexOf(']');
            if (close <= 1) return false;
            host = value[1..close];
            var rest = value[(close + 1)..];
            if (rest.Length > 0)
            {
                if (!rest.StartsWith(':')) return false;
                if (!int.TryParse(rest[1..], out port) || port is < 1 or > 65535) return false;
            }
            if (!IsValidHost(host, allowColons: true)) return false;
        }
        else
        {
            var colon = value.LastIndexOf(':');
            if (colon >= 0)
            {
                if (colon == 0 || colon == value.Length - 1) return false;
                if (!int.TryParse(value[(colon + 1)..], out port) || port is < 1 or > 65535) return false;
                host = value[..colon];
            }
            else
            {
                host = value;
            }
            if (!IsValidHost(host, allowColons: false)) return false;
        }

        target = new ConnectionTarget(host, port);
        return true;
    }

    public override string ToString() => Host.Contains(':') && !Host.StartsWith('[')
        ? $"[{Host}]:{Port}"
        : $"{Host}:{Port}";

    private static bool IsValidHost(string host, bool allowColons)
    {
        if (host.Length == 0 || host.Any(char.IsWhiteSpace)) return false;
        if (!allowColons && host.Contains(':')) return false;
        return host.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or ':');
    }
}
