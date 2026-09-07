using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Drawer.Windows;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);
    public static void SetNonActivating(Window window, bool value)
    {
        nint hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return;
        long style = (long)GetWindowLongPtr(hwnd, -20);
        style = window.ShowInTaskbar ? style & ~0x80L : style | 0x80; // Tool window outside inspection mode.
        style = value ? style | 0x08000000 : style & ~0x08000000L;
        SetWindowLongPtr(hwnd, -20, (nint)style);
        SetWindowPos(hwnd, new nint(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x20);
    }
    public static bool OurAppIsForeground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        return pid == Environment.ProcessId;
    }
}
