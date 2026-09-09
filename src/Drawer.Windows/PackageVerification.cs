using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Drawer.Windows;

internal static class PackageVerification
{
    public static int Run(string reportPath)
    {
        try
        {
            var text = new FormattedText("drawer 中文", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 14, Brushes.Black, 1);
            if (text.Width <= 0) throw new InvalidOperationException("WPF text rendering failed");
            var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var png = new MemoryStream(); encoder.Save(png); png.Position = 0;
            var decoded = BitmapDecoder.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (decoded.Frames[0].PixelWidth != 2) throw new InvalidOperationException("WPF image codecs failed");
            if (DrawerState.Deserialize(new DrawerState().Serialize()).SchemaVersion != DrawerState.CurrentSchema)
                throw new InvalidOperationException("Embedded core assembly failed");
            var workspace = new DrawerWorkspace(); workspace.Add(new(DockEdge.Left, .25));
            if (DrawerWorkspace.Deserialize(workspace.Serialize()).Drawers.Count != 2)
                throw new InvalidOperationException("Multi-drawer serialization failed");
            var iconSizes = AppIcon.WindowIcon.Decoder!.Frames.Select(frame => frame.PixelWidth).Order().ToArray();
            if (!iconSizes.Contains(16) || !iconSizes.Contains(32) || !iconSizes.Contains(256))
                throw new InvalidOperationException("Embedded application icon sizes are missing");
            using var trayIcon = AppIcon.CreateTrayIcon();
            if (trayIcon.Handle == IntPtr.Zero) throw new InvalidOperationException("Notification icon could not be loaded");
            using var executableIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
            if (executableIcon is null) throw new InvalidOperationException("Executable icon could not be loaded");
            // Single-file hosts may statically link CoreCLR rather than load coreclr.dll.
            string[] nativeModules = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .Where(m => m.ModuleName.Contains("clr", StringComparison.OrdinalIgnoreCase) ||
                    m.ModuleName.Contains("PresentationNative", StringComparison.OrdinalIgnoreCase) ||
                    m.ModuleName.Contains("wpfgfx", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.FileName).ToArray();
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new
            {
                Success = true,
                Version = typeof(App).Assembly.GetName().Version?.ToString(),
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                RuntimeVersion = Environment.Version.ToString(),
                RuntimeDirectory = RuntimeEnvironment.GetRuntimeDirectory(),
                NativeModulePaths = nativeModules,
                WpfText = "passed", WpfPng = "passed", EmbeddedCore = "passed", MultiDrawer = "passed",
                ApplicationIcon = "passed", IconSizes = iconSizes
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Success = false, Error = ex.ToString() }));
            return 1;
        }
    }
}
