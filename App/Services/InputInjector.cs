using System.Runtime.InteropServices;
using Mconect.Core.Protocol;
using Mconect.Core.Remoting;

namespace Mconect.App.Services;

public sealed class InputInjector : IInputSink
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseWheel = 0x0800;
    private const uint MouseAbsolute = 0x8000;
    private const uint KeyUp = 0x0002;
    private const uint KeyExtended = 0x0001;

    private static readonly HashSet<int> ExtendedKeys = new()
    {
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28,
        0x2C, 0x2D, 0x2E, 0x5B, 0x5C, 0x5D, 0x6F, 0x90,
        0xA3, 0xA5
    };

    public void Mouse(MouseMessage message)
    {
        var flags = MouseMove | MouseAbsolute;
        var wheelData = 0;

        switch (message.Action)
        {
            case MouseAction.Move:
                break;
            case MouseAction.LeftDown:
                flags |= MouseLeftDown;
                break;
            case MouseAction.LeftUp:
                flags |= MouseLeftUp;
                break;
            case MouseAction.RightDown:
                flags |= MouseRightDown;
                break;
            case MouseAction.RightUp:
                flags |= MouseRightUp;
                break;
            case MouseAction.MiddleDown:
                flags |= MouseMiddleDown;
                break;
            case MouseAction.MiddleUp:
                flags |= MouseMiddleUp;
                break;
            case MouseAction.Wheel:
                flags |= MouseWheel;
                wheelData = message.Wheel;
                break;
        }

        var input = new Input
        {
            Type = InputMouse,
            Data = new InputUnion
            {
                Mouse = new MouseInput
                {
                    Dx = message.X,
                    Dy = message.Y,
                    MouseData = (uint)wheelData,
                    Flags = flags
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }

    public void Key(KeyMessage message)
    {
        var flags = message.Down ? 0u : KeyUp;
        if (ExtendedKeys.Contains(message.VirtualKey))
        {
            flags |= KeyExtended;
        }

        var input = new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = (ushort)message.VirtualKey,
                    Flags = flags
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int size);
}
