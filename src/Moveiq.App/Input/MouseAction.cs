using System.Runtime.InteropServices;

namespace Moveiq.Input;

public enum MouseAction
{
    PassThrough,
    Copy,
    Paste,
    Undo,
    Redo,
    TaskSwitcher,
    ClipboardHistory,
    BrowserBack,
    BrowserForward,
    Screenshot,
    LaunchApp
}

public static class KeyboardActionSender
{
    public static void Send(MouseAction action)
    {
        var keys = action switch
        {
            MouseAction.Copy => new ushort[] { VkControl, (ushort)'C' },
            MouseAction.Paste => new ushort[] { VkControl, (ushort)'V' },
            MouseAction.Undo => new ushort[] { VkControl, (ushort)'Z' },
            MouseAction.Redo => new ushort[] { VkControl, (ushort)'Y' },
            MouseAction.TaskSwitcher => new ushort[] { VkAlt, VkTab },
            MouseAction.ClipboardHistory => new ushort[] { VkLWin, (ushort)'V' },
            MouseAction.BrowserBack => new ushort[] { VkAlt, VkLeft },
            MouseAction.BrowserForward => new ushort[] { VkAlt, VkRight },
            MouseAction.Screenshot => new ushort[] { VkLWin, VkShift, (ushort)'S' },
            _ => []
        };

        if (keys.Length == 0) return;

        var inputs = new Input[keys.Length * 2];
        for (var index = 0; index < keys.Length; index++)
            inputs[index] = KeyInput(keys[index], keyUp: false);
        for (var index = keys.Length - 1; index >= 0; index--)
            inputs[keys.Length + (keys.Length - 1 - index)] = KeyInput(keys[index], keyUp: true);

        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private static Input KeyInput(ushort key, bool keyUp) => new()
    {
        Type = 1,
        Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = keyUp ? 0x0002u : 0u } }
    };

    public static void ReplayMouseClick(string button)
    {
        if (!MouseButtonFlags.TryGetValue(button, out var flags)) return;
        var inputs = new[] { MouseInputEvent(0, 0, flags.Data, flags.Down), MouseInputEvent(0, 0, flags.Data, flags.Up) };
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private static Input MouseInputEvent(int x, int y, uint data, uint flags) => new()
    {
        Type = 0,
        Data = new InputUnion { Mouse = new MouseInput { Dx = x, Dy = y, MouseData = data, Flags = flags } }
    };

    private static readonly Dictionary<string, (uint Down, uint Up, uint Data)> MouseButtonFlags = new(StringComparer.Ordinal)
    {
        ["Left"] = (0x0002, 0x0004, 0),
        ["Right"] = (0x0008, 0x0010, 0),
        ["Middle"] = (0x0020, 0x0040, 0),
        ["Back"] = (0x0080, 0x0100, 1),
        ["Forward"] = (0x0080, 0x0100, 2)
    };

    private const ushort VkTab = 0x09, VkControl = 0x11, VkAlt = 0x12, VkLWin = 0x5B, VkLeft = 0x25, VkRight = 0x27, VkShift = 0x10;

    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public IntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
