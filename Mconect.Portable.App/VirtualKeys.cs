using Avalonia.Input;

namespace Mconect.Portable.App;

/// <summary>Traducción de teclas de Avalonia a códigos virtuales de Windows para el anfitrión.</summary>
internal static class VirtualKeys
{
    public static int FromAvalonia(Key key)
    {
        // Avalonia no garantiza que los valores del enum sean contiguos: se Traduce por nombre.
        var name = key.ToString();
        if (name.Length == 1 && char.IsAsciiLetter(name[0])) return char.ToUpperInvariant(name[0]);
        if (name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1])) return name[1];
        if (name.StartsWith("NumPad", StringComparison.Ordinal) &&
            name.Length == 8 && char.IsAsciiDigit(name[6])) return 0x60 + (name[6] - '0');
        if (name.Length == 2 && name[0] == 'F' && char.IsAsciiDigit(name[1])) return 0x70 + (name[1] - '0');
        if (name.Length == 3 && name[0] == 'F' && char.IsAsciiDigit(name[1]) && char.IsAsciiDigit(name[2]))
        {
            return 0x70 + ((name[1] - '0') * 10 + (name[2] - '0'));
        }

        return key switch
        {
            Key.Back => 0x08,
            Key.Tab => 0x09,
            Key.Enter => 0x0D,
            Key.LeftShift => 0xA0,
            Key.RightShift => 0xA1,
            Key.LeftCtrl => 0xA2,
            Key.RightCtrl => 0xA3,
            Key.LeftAlt => 0xA4,
            Key.RightAlt => 0xA5,
            Key.Escape => 0x1B,
            Key.Space => 0x20,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.End => 0x23,
            Key.Home => 0x24,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.OemSemicolon => 0xBA,
            Key.OemPlus => 0xBB,
            Key.OemComma => 0xBC,
            Key.OemMinus => 0xBD,
            Key.OemPeriod => 0xBE,
            Key.OemQuestion => 0xBF,
            Key.OemQuotes => 0xDE,
            Key.OemOpenBrackets => 0xDB,
            Key.OemCloseBrackets => 0xDD,
            Key.OemBackslash => 0xDC,
            _ => 0
        };
    }
}
