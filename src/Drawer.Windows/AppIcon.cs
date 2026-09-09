using System.Windows;
using System.Windows.Media.Imaging;

namespace Drawer.Windows;

internal static class AppIcon
{
    private static readonly Uri IconUri = new("pack://application:,,,/drawer;component/Assets/DrawerIcon.ico");
    public static BitmapFrame WindowIcon { get; } = LoadWindowIcon();
    public static BitmapFrame AboutIcon { get; } = LoadAboutIcon();

    private static BitmapFrame LoadAboutIcon()
    {
        var image = BitmapDecoder.Create(IconUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames.MaxBy(frame => frame.PixelWidth)!;
        image.Freeze(); return image;
    }

    private static BitmapFrame LoadWindowIcon()
    {
        var image = BitmapFrame.Create(IconUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        image.Freeze(); return image;
    }
    public static System.Drawing.Icon CreateTrayIcon()
    {
        using var stream = Application.GetResourceStream(IconUri)!.Stream;
        using var icon = new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
        return (System.Drawing.Icon)icon.Clone();
    }
}
