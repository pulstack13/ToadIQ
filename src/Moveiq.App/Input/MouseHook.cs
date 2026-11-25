using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Moveiq.Input;

public sealed record MouseEventInfo(DateTime Time, string Name, string Details, long Timestamp, int X, int Y);

public sealed class MouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201, WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204, WmRButtonUp = 0x0205;
    private const int WmMButtonDown = 0x0207, WmMButtonUp = 0x0208;
    private const int WmMouseMove = 0x0200;
    private const int WmMouseWheel = 0x020A, WmXButtonDown = 0x020B, WmXButtonUp = 0x020C;
    private const int WmMouseHWheel = 0x020E;
    private readonly HookProc _callback;
    private readonly Func<MouseEventInfo, bool> _onEvent;
    private IntPtr _handle;

    public MouseHook(Func<MouseEventInfo, bool> onEvent)
    {
        _onEvent = onEvent;
        _callback = Callback;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule ?? throw new InvalidOperationException("Could not locate the application module.");
        _handle = SetWindowsHookEx(WhMouseLl, _callback, GetModuleHandle(module.ModuleName), 0);
        if (_handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = unchecked((int)wParam.ToInt64());
            var data = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            var isInjected = (data.Flags & 0x00000001) != 0;
            var name = message switch
            {
                WmMouseMove => "Mouse move",
                WmLButtonDown => "Left button down", WmLButtonUp => "Left button up",
                WmRButtonDown => "Right button down", WmRButtonUp => "Right button up",
                WmMButtonDown => "Middle button down", WmMButtonUp => "Middle button up",
                WmXButtonDown => "X button down", WmXButtonUp => "X button up",
                WmMouseWheel => "Wheel", WmMouseHWheel => "Horizontal wheel", _ => null
            };
            if (!isInjected && name is not null)
            {
                var details = message is WmXButtonDown or WmXButtonUp
                    ? $"{((data.MouseData >> 16) == 1 ? "XBUTTON1" : "XBUTTON2")} · {data.Point.X}, {data.Point.Y}"
                    : message is WmMouseWheel or WmMouseHWheel
                        ? $"Delta {(short)(data.MouseData >> 16)} · {data.Point.X}, {data.Point.Y}"
                        : $"{data.Point.X}, {data.Point.Y}";
                var shouldSuppress = _onEvent(new MouseEventInfo(DateTime.Now, name, details, Stopwatch.GetTimestamp(), data.Point.X, data.Point.Y));
                if (shouldSuppress) return new IntPtr(1);
            }
        }
        return CallNextHookEx(_handle, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        UnhookWindowsHookEx(_handle);
        _handle = IntPtr.Zero;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MsllHookStruct { public Point Point; public uint MouseData, Flags, Time; public IntPtr ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string? moduleName);
}
