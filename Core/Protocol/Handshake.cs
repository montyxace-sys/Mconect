using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Mconect.Core.Protocol;

/// <summary>Interceptor de la contraseña de sesión mediante ECDH P-256 y HMAC-SHA256.</summary>
public static class HandshakeProtocol
{
    public const byte Version = 1;
    public const byte MessageClientHello = 1;
    public const byte MessageServerHello = 2;
    public const byte MessageClientAuth = 3;
    public const byte MessageAuthResult = 4;
    public const byte MessageConsent = 5;

    private const int NonceSize = 16;
    private const int SaltSize = 32;
    private const int MaxHandshakeSize = 4096;
    private static readonly byte[] Magic = "MCON"u8.ToArray();
    private static readonly byte[] SessionInfo = "Mconect v1 session"u8.ToArray();
    private static readonly byte[] AuthInfo = "mconect-auth"u8.ToArray();

    public static byte[] DeriveSessionKey(byte[] sharedSecret, byte[] salt)
    {
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, salt, SessionInfo);
    }

    public static byte[] ComputeProof(byte[] sessionKey, byte[] clientNonce, byte[] serverNonce, string password)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password ?? string.Empty);
        var data = new byte[AuthInfo.Length + clientNonce.Length + serverNonce.Length + passwordBytes.Length];
        AuthInfo.CopyTo(data, 0);
        clientNonce.CopyTo(data, AuthInfo.Length);
        serverNonce.CopyTo(data, AuthInfo.Length + clientNonce.Length);
        passwordBytes.CopyTo(data, AuthInfo.Length + clientNonce.Length + serverNonce.Length);
        return HMACSHA256.HashData(sessionKey, data);
    }

    public static void WriteMessage(Stream stream, byte type, ReadOnlySpan<byte> body)
    {
        var payload = new byte[Magic.Length + 2 + body.Length];
        Magic.CopyTo(payload, 0);
        payload[4] = Version;
        payload[5] = type;
        body.CopyTo(payload.AsSpan(6));
        FrameCodec.Write(stream, payload);
    }

    public static byte[] ReadMessage(Stream stream, byte expectedType)
    {
        var payload = FrameCodec.Read(stream)
            ?? throw new InvalidDataException("El otro extremo cerró la conexión durante el saludo.");
        if (payload.Length < 6 || payload.Length > MaxHandshakeSize ||
            !payload.AsSpan(0, 4).SequenceEqual(Magic) || payload[4] != Version)
        {
            throw new InvalidDataException("No es un saludo de Mconect válido.");
        }

        if (payload[5] != expectedType)
        {
            throw new InvalidDataException("Mensaje de saludo inesperado.");
        }

        return payload[6..];
    }

    public static byte[] BuildClientHello(byte[] nonce, byte[] publicKey)
    {
        var body = new byte[NonceSize + 4 + publicKey.Length];
        nonce.CopyTo(body, 0);
        BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(NonceSize), publicKey.Length);
        publicKey.CopyTo(body, NonceSize + 4);
        return body;
    }

    public static (byte[] Nonce, byte[] PublicKey) ParseHello(byte[] body)
    {
        if (body.Length < NonceSize + 4)
        {
            throw new InvalidDataException("Saludo incompleto.");
        }

        var keyLength = BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(NonceSize));
        if (keyLength <= 0 || keyLength > MaxHandshakeSize || body.Length != NonceSize + 4 + keyLength)
        {
            throw new InvalidDataException("Clave pública inválida.");
        }

        return (body[..NonceSize], body[(NonceSize + 4)..]);
    }

    public static byte[] BuildServerHello(byte[] nonce, byte[] publicKey, byte[] salt)
    {
        var hello = BuildClientHello(nonce, publicKey);
        var body = new byte[hello.Length + SaltSize];
        hello.CopyTo(body, 0);
        salt.CopyTo(body, hello.Length);
        return body;
    }

    public static (byte[] Nonce, byte[] PublicKey, byte[] Salt) ParseServerHello(byte[] body)
    {
        if (body.Length < NonceSize + 4 + SaltSize)
        {
            throw new InvalidDataException("Saludo del servidor incompleto.");
        }

        var keyLength = BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(NonceSize));
        if (keyLength <= 0 || keyLength > MaxHandshakeSize ||
            body.Length != NonceSize + 4 + keyLength + SaltSize)
        {
            throw new InvalidDataException("Clave pública del servidor inválida.");
        }

        return (body[..NonceSize], body[(NonceSize + 4)..(NonceSize + 4 + keyLength)], body[^SaltSize..]);
    }

    public static byte[] BuildAuthResult(bool accepted, string reason)
    {
        var reasonBytes = Encoding.UTF8.GetBytes(reason ?? string.Empty);
        var body = new byte[1 + 2 + reasonBytes.Length];
        body[0] = accepted ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(1), (ushort)Math.Min(ushort.MaxValue, reasonBytes.Length));
        reasonBytes.AsSpan(0, Math.Min(reasonBytes.Length, ushort.MaxValue)).CopyTo(body.AsSpan(3));
        return body;
    }

    public static (bool Accepted, string Reason) ParseAuthResult(byte[] body)
    {
        if (body.Length < 3)
        {
            throw new InvalidDataException("Resultado de autenticación inválido.");
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(1));
        var reason = length <= body.Length - 3 ? Encoding.UTF8.GetString(body, 3, length) : string.Empty;
        return (body[0] == 1, reason);
    }
}

public sealed class ClientHandshake
{
    public byte[] SessionKey { get; private set; } = Array.Empty<byte>();
    public string Error { get; private set; } = string.Empty;
    public bool Accepted { get; private set; }

    public bool Run(Stream stream, string password)
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var clientNonce = RandomNumberGenerator.GetBytes(16);
        var clientPublic = ecdh.ExportSubjectPublicKeyInfo();

        HandshakeProtocol.WriteMessage(stream, HandshakeProtocol.MessageClientHello,
            HandshakeProtocol.BuildClientHello(clientNonce, clientPublic));

        var serverBody = HandshakeProtocol.ReadMessage(stream, HandshakeProtocol.MessageServerHello);
        var (serverNonce, serverPublicKey, salt) = HandshakeProtocol.ParseServerHello(serverBody);

        using var peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(serverPublicKey, out _);
        var shared = ecdh.DeriveRawSecretAgreement(peer.PublicKey);
        SessionKey = HandshakeProtocol.DeriveSessionKey(shared, salt);

        var proof = HandshakeProtocol.ComputeProof(SessionKey, clientNonce, serverNonce, password);
        HandshakeProtocol.WriteMessage(stream, HandshakeProtocol.MessageClientAuth, proof);

        var resultBody = HandshakeProtocol.ReadMessage(stream, HandshakeProtocol.MessageAuthResult);
        var (accepted, reason) = HandshakeProtocol.ParseAuthResult(resultBody);
        Accepted = accepted;
        Error = reason;
        return accepted;
    }
}

public sealed class ServerHandshake
{
    public byte[] SessionKey { get; private set; } = Array.Empty<byte>();

    public bool Run(Stream stream, string password, out string error)
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var hello = HandshakeProtocol.ReadMessage(stream, HandshakeProtocol.MessageClientHello);
        var (clientNonce, clientPublicKey) = HandshakeProtocol.ParseHello(hello);

        var serverNonce = RandomNumberGenerator.GetBytes(16);
        var salt = RandomNumberGenerator.GetBytes(32);
        var serverPublic = ecdh.ExportSubjectPublicKeyInfo();

        using var peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(clientPublicKey, out _);
        var shared = ecdh.DeriveRawSecretAgreement(peer.PublicKey);
        SessionKey = HandshakeProtocol.DeriveSessionKey(shared, salt);

        HandshakeProtocol.WriteMessage(stream, HandshakeProtocol.MessageServerHello,
            HandshakeProtocol.BuildServerHello(serverNonce, serverPublic, salt));

        var proof = HandshakeProtocol.ReadMessage(stream, HandshakeProtocol.MessageClientAuth);
        var expected = HandshakeProtocol.ComputeProof(SessionKey, clientNonce, serverNonce, password);
        if (!CryptographicOperations.FixedTimeEquals(proof, expected))
        {
            error = "Contraseña incorrecta.";
            HandshakeProtocol.WriteMessage(stream, HandshakeProtocol.MessageAuthResult,
                HandshakeProtocol.BuildAuthResult(false, error));
            return false;
        }

        error = string.Empty;
        HandshakeProtocol.WriteMessage(stream, HandshakeProtocol.MessageAuthResult,
            HandshakeProtocol.BuildAuthResult(true, ""));
        return true;
    }
}
