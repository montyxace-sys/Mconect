using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Mnet.Core.Protocol;

/// <summary>Longitud prefijada y cifrado AES-256-GCM de los mensajes de sesión.</summary>
public static class FrameCodec
{
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int MaxFrameSize = 8 * 1024 * 1024;

    public static byte[] Seal(AesGcm aes, ReadOnlySpan<byte> plaintext)
    {
        var frame = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = frame.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        aes.Encrypt(nonce, plaintext, frame.AsSpan(NonceSize, plaintext.Length),
            frame.AsSpan(NonceSize + plaintext.Length, TagSize));
        return frame;
    }

    public static byte[]? TryOpen(AesGcm aes, ReadOnlySpan<byte> frame)
    {
        if (frame.Length < NonceSize + TagSize)
        {
            return null;
        }

        var plaintext = new byte[frame.Length - NonceSize - TagSize];
        try
        {
            aes.Decrypt(frame[..NonceSize], frame.Slice(NonceSize, plaintext.Length),
                frame[^TagSize..], plaintext);
            return plaintext;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static void Write(Stream stream, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxFrameSize)
        {
            throw new InvalidOperationException("El mensaje es demasiado grande.");
        }

        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        stream.Write(header);
        stream.Write(payload);
        stream.Flush();
    }

    /// <summary>Devuelve null si el otro extremo cerró la conexión limpiamente.</summary>
    public static byte[]? Read(Stream stream)
    {
        Span<byte> header = stackalloc byte[4];
        if (!ReadExactly(stream, header))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 0 || length > MaxFrameSize)
        {
            throw new InvalidDataException("Trama de tamaño inválido.");
        }

        var payload = new byte[length];
        if (!ReadExactly(stream, payload))
        {
            throw new InvalidDataException("La conexión se cortó a mitad de una trama.");
        }

        return payload;
    }

    private static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var chunk = stream.Read(buffer[read..]);
            if (chunk <= 0)
            {
                return read == 0 ? false : throw new InvalidDataException("Trama incompleta.");
            }

            read += chunk;
        }

        return true;
    }
}
