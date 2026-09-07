using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Drawer.Windows;

internal static class ShellIcon
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfo(string path, uint attributes, out SHFILEINFO info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    public static ImageSource? Load(string path)
    {
        nint icon = 0;
        try
        {
            SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100);
            icon = info.Icon;
            if (icon == 0) return null;
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze(); return bitmap;
        }
        catch { return null; }
        finally { if (icon != 0) DestroyIcon(icon); }
    }
}
