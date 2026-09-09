using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Drawer.Windows;

public sealed class GlobalShortcutService : IShortcutRegistration, IDisposable
{
    private readonly HwndSource source;
    private readonly Action<Guid> invoked;
    public DrawerShortcuts Bindings { get; }
    public Action<ShortcutBinding>? Recorder { get; set; }

    public GlobalShortcutService(Action<Guid> invoked)
    {
        this.invoked = invoked;
        source = new HwndSource(new HwndSourceParameters("drawer.shortcuts") { ParentWindow = new nint(-3) });
        Bindings = new(this);
        source.AddHook(OnMessage);
    }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
    public bool Register(int id, ShortcutBinding binding) => RegisterHotKey(source.Handle, id, (uint)binding.Modifiers | 0x4000, (uint)binding.VirtualKey);
    public void Unregister(int id) => UnregisterHotKey(source.Handle, id);
    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && Bindings.Resolve((int)wParam) is { } drawerId)
        {
            handled = true;
            if (Recorder is { } record && Bindings.GetActive(drawerId) is { } binding) record(binding);
            else invoked(drawerId);
        }
        return 0;
    }
    public void Dispose()
    {
        Recorder = null; Bindings.Dispose(); source.RemoveHook(OnMessage); source.Dispose();
    }
}
