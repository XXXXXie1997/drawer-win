using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Diagnostics;

namespace Drawer.Windows;

// Shell links supply best-effort Windows tracking. Paths remain the portable fallback.
public sealed class FileReferenceService
{
    private readonly string directory;
    public FileReferenceService(string root) { directory = Path.Combine(root, "references"); Directory.CreateDirectory(directory); }
    public DrawerItem Create(string path)
    {
        var item = new DrawerItem { Kind = ItemKind.FileReference, Path = path, DisplayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), Frame = new(default, new(3, 3)) };
        object? com = null;
        try
        {
            com = new ShellLink();
            ((IShellLinkW)com).SetPath(path);
            item.ReferenceId = item.Id.ToString("N");
            ((IPersistFile)com).Save(Path.Combine(directory, item.ReferenceId + ".lnk"), true);
        }
        catch { item.ReferenceId = null; }
        finally { if (com is not null) Marshal.FinalReleaseComObject(com); }
        return item;
    }
    public string? Resolve(DrawerItem item)
    {
        // Avoid blocking Shell searches for network/offline paths. Full relinking is a later feature.
        if (File.Exists(item.Path) || Directory.Exists(item.Path)) return item.Path;
        if (item.ReferenceId is null || !Guid.TryParseExact(item.ReferenceId, "N", out _)) return null;
        object? com = null;
        try
        {
            string link = Path.Combine(directory, item.ReferenceId + ".lnk");
            if (!File.Exists(link)) return null;
            com = new ShellLink();
            ((IPersistFile)com).Load(link, 0);
            var shell = (IShellLinkW)com;
            shell.Resolve(0, 1 | 16 | (100u << 16)); // No UI, no broad search; bounded resolve timeout.
            var buffer = new StringBuilder(32768);
            shell.GetPath(buffer, buffer.Capacity, 0, 0);
            string result = buffer.ToString();
            return File.Exists(result) || Directory.Exists(result) ? result : null;
        }
        catch { return null; }
        finally { if (com is not null) Marshal.FinalReleaseComObject(com); }
    }
    public static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    public static void Reveal(string path) => Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class ShellLink { }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, nint findData, uint flags);
        void GetIDList(out nint pidl);
        void SetIDList(nint pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int cmd);
        void SetShowCmd(int cmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(nint hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
