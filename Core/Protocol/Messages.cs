using System.Buffers.Binary;

namespace Mnet.Core.Protocol;

public enum MessageType : byte
{
    Screen = 1,
    Mouse = 2,
    Key = 3,
    Ping = 4,
    Bye = 5,
    Consent = 6
}

public enum MouseAction : byte
{
    Move = 0,
    LeftDown = 1,
    LeftUp = 2,
    RightDown = 3,
    RightUp = 4,
    MiddleDown = 5,
    MiddleUp = 6,
    Wheel = 7
}

public sealed class ScreenMessage
{
    public int FrameWidth { get; init; }
    public int FrameHeight { get; init; }
    public int ScreenWidth { get; init; }
    public int ScreenHeight { get; init; }
    public long TimestampMs { get; init; }
    public byte[] Jpeg { get; init; } = Array.Empty<byte>();
}

public readonly struct MouseMessage
{
    public MouseMessage(MouseAction action, ushort x, ushort y, int wheel)
    {
        Action = action;
        X = x;
        Y = y;
        Wheel = wheel;
    }

    public MouseAction Action { get; }
    public ushort X { get; }
    public ushort Y { get; }
    public int Wheel { get; }
}

public readonly struct KeyMessage
{
    public KeyMessage(bool down, int virtualKey)
    {
        Down = down;
        VirtualKey = virtualKey;
    }

    public bool Down { get; }
    public int VirtualKey { get; }
}

/// <summary>Serialización binaria de los mensajes que viajan cifrados dentro de la sesión.</summary>
public static class SessionMessages
{
    private const int ScreenHeaderSize = 25;

    public static byte[] EncodeScreen(ScreenMessage screen)
    {
        var payload = new byte[ScreenHeaderSize + screen.Jpeg.Length];
        payload[0] = (byte)MessageType.Screen;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1), screen.FrameWidth);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(5), screen.FrameHeight);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(9), screen.ScreenWidth);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(13), screen.ScreenHeight);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(17), screen.TimestampMs);
        screen.Jpeg.CopyTo(payload, ScreenHeaderSize);
        return payload;
    }

    public static ScreenMessage DecodeScreen(byte[] payload)
    {
        if (payload.Length < ScreenHeaderSize || payload[0] != (byte)MessageType.Screen)
        {
            throw new InvalidDataException("Trama de pantalla inválida.");
        }

        return new ScreenMessage
        {
            FrameWidth = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(1)),
            FrameHeight = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(5)),
            ScreenWidth = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(9)),
            ScreenHeight = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(13)),
            TimestampMs = BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(17)),
            Jpeg = payload[ScreenHeaderSize..]
        };
    }

    public static byte[] EncodeMouse(MouseMessage mouse)
    {
        var payload = new byte[10];
        payload[0] = (byte)MessageType.Mouse;
        payload[1] = (byte)mouse.Action;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), mouse.X);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), mouse.Y);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(6), mouse.Wheel);
        return payload;
    }

    public static MouseMessage DecodeMouse(byte[] payload)
    {
        if (payload.Length != 10 || payload[0] != (byte)MessageType.Mouse)
        {
            throw new InvalidDataException("Mensaje de ratón inválido.");
        }

        var action = (MouseAction)payload[1];
        if (action > MouseAction.Wheel)
        {
            throw new InvalidDataException("Acción de ratón desconocida.");
        }

        return new MouseMessage(action,
            BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(2)),
            BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(4)),
            BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(6)));
    }

    public static byte[] EncodeKey(KeyMessage key)
    {
        var payload = new byte[6];
        payload[0] = (byte)MessageType.Key;
        payload[1] = key.Down ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(2), key.VirtualKey);
        return payload;
    }

    public static KeyMessage DecodeKey(byte[] payload)
    {
        if (payload.Length != 6 || payload[0] != (byte)MessageType.Key)
        {
            throw new InvalidDataException("Mensaje de teclado inválido.");
        }

        return new KeyMessage(payload[1] == 1, BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(2)));
    }

    public static byte[] EncodePing(long ticks)
    {
        var payload = new byte[9];
        payload[0] = (byte)MessageType.Ping;
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(1), ticks);
        return payload;
    }

    public static long DecodePing(byte[] payload)
    {
        if (payload.Length != 9 || payload[0] != (byte)MessageType.Ping)
        {
            throw new InvalidDataException("Latido inválido.");
        }

        return BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(1));
    }

    public static byte[] EncodeBye(string reason)
    {
        var text = System.Text.Encoding.UTF8.GetBytes(reason ?? string.Empty);
        var payload = new byte[3 + text.Length];
        payload[0] = (byte)MessageType.Bye;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1), (ushort)text.Length);
        text.CopyTo(payload, 3);
        return payload;
    }

    public static string DecodeBye(byte[] payload)
    {
        if (payload.Length < 3 || payload[0] != (byte)MessageType.Bye)
        {
            throw new InvalidDataException("Mensaje de cierre inválido.");
        }

        var length = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(1));
        return length <= payload.Length - 3 ? System.Text.Encoding.UTF8.GetString(payload, 3, length) : string.Empty;
    }

    public static byte[] EncodeConsent(bool accepted, string reason)
    {
        var text = System.Text.Encoding.UTF8.GetBytes(reason ?? string.Empty);
        var payload = new byte[4 + text.Length];
        payload[0] = (byte)MessageType.Consent;
        payload[1] = accepted ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), (ushort)text.Length);
        text.CopyTo(payload, 4);
        return payload;
    }

    public static (bool Accepted, string Reason) DecodeConsent(byte[] payload)
    {
        if (payload.Length < 4 || payload[0] != (byte)MessageType.Consent)
        {
            throw new InvalidDataException("Respuesta de consentimiento inválida.");
        }

        var length = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(2));
        var reason = length <= payload.Length - 4
            ? System.Text.Encoding.UTF8.GetString(payload, 4, length)
            : string.Empty;
        return (payload[1] == 1, reason);
    }
}
