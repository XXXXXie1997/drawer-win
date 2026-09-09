using Drawer.Core;
using Drawer.Windows;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class AnimationChecks
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(App app)
    {
        foreach (var edge in Enum.GetValues<DockEdge>())
        foreach (double position in new[] { 0d, .5, 1d })
        foreach (var dimensions in new[] { (420d, 560d), (1800d, 160d), (160d, 1200d), (10000d, 10000d), (10d, 90d), (90d, 10d), (10d, 10d) })
        {
            var record = new DrawerRecord(); record.State.Preferences.Edge = edge; record.State.Preferences.EdgePosition = position;
            record.State.Preferences.Width = dimensions.Item1; record.State.Preferences.Height = dimensions.Item2;
            if (dimensions.Item1 <= 90) DrawerSizing.SetPercentages(record.State.Preferences, dimensions.Item1, dimensions.Item2);
            bool checkPixels = dimensions.Item1 == 420;
            var host = new DrawerHost(app, record); var window = host.Window;
            try
            {
                window.Position(false);
                var fixedBounds = new Rect(window.Left, window.Top, window.Width, window.Height);
                CheckFrame(window, fixedBounds, edge);
                if (checkPixels) CheckPixels(window, edge, true);
                foreach (var mode in new[] { DrawerMode.Handle, DrawerMode.Focused, DrawerMode.Hidden, DrawerMode.Handle, DrawerMode.Focused, DrawerMode.Hidden })
                {
                    typeof(EdgeWindow).GetProperty(nameof(EdgeWindow.Mode))!.SetValue(window, mode);
                    window.Position();
                    // RenderTargetBitmap can pump WPF rendering; use only our deterministic clock.
                    typeof(EdgeWindow).GetMethod("StopAnimation", Private)!.Invoke(window, null);
                    // Interrupt earlier transitions, then allow the final collapse to finish.
                    int frames = mode == DrawerMode.Hidden ? 90 : 12;
                    for (int frame = 0; frame < frames; frame++)
                    {
                        typeof(EdgeWindow).GetMethod("AdvanceAnimation", Private)!.Invoke(window, [1d / 120]);
                        CheckFrame(window, fixedBounds, edge);
                        if (checkPixels && frame == 6) CheckPixels(window, edge, false);
                    }
                }
                if (checkPixels) CheckPixels(window, edge, true);
                double thickness = edge == DockEdge.Top ? window.VisibleBounds.Height : window.VisibleBounds.Width;
                Require(Math.Abs(thickness - DockPlacement.HintThickness) < .01, "Resting hint has the configured thickness");
                var label = (HandleLabel)typeof(EdgeWindow).GetField("handleLabel", Private)!.GetValue(window)!;
                Require(label.Opacity == 0, "Resting hint has no label");
            }
            finally { window.CloseDrawer(); }
        }
    }
    private static void CheckFrame(EdgeWindow window, Rect fixedBounds, DockEdge edge)
    {
        Require(fixedBounds == new Rect(window.Left, window.Top, window.Width, window.Height), "Native host must not move or resize during animation");
        var bounds = window.VisibleBounds;
        Require(bounds.Left >= -.01 && bounds.Top >= -.01 && bounds.Right <= window.Width + .01 && bounds.Bottom <= window.Height + .01, "Visible shape stays in its fixed host");
        Require(edge switch { DockEdge.Top => bounds.Top == 0, DockEdge.Left => bounds.Left == 0, _ => Math.Abs(bounds.Right - window.Width) < .01 }, "Docked edge stays fixed on every frame");
    }
    private static void CheckPixels(EdgeWindow window, DockEdge edge, bool hint)
    {
        var root = (FrameworkElement)window.Content;
        int width = (int)Math.Round(window.Width), height = (int)Math.Round(window.Height);
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(root);
        var pixels = new byte[width * height * 4]; image.CopyPixels(pixels, width * 4, 0);
        var bounds = window.VisibleBounds;
        int x = edge == DockEdge.Right ? width - 1 : edge == DockEdge.Left ? 0 : (int)(bounds.X + bounds.Width / 2);
        int y = edge == DockEdge.Top ? 0 : (int)(bounds.Y + bounds.Height / 2);
        var shell = (Border)((System.Windows.Controls.Canvas)root).Children[0];
        Require(pixels[(y * width + x) * 4 + 3] > 0, $"Rendered pixels touch the screen edge: edge={edge}, mode={window.Mode}, hint={hint}, bounds={bounds}, host={width}x{height}, actual={shell.ActualWidth}x{shell.ActualHeight}, offset={VisualTreeHelper.GetOffset(shell)}, sample={x},{y}, desired={shell.DesiredSize}, slot={System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(shell)}, canvas={System.Windows.Controls.Canvas.GetLeft(shell)},{System.Windows.Controls.Canvas.GetTop(shell)}, margin={shell.Margin}");
        for (int py = 0; py < height; py++)
        for (int px = 0; px < width; px++)
            if (px + 1 < bounds.Left || px > bounds.Right || py + 1 < bounds.Top || py > bounds.Bottom)
                Require(pixels[(py * width + px) * 4 + 3] == 0, "Unused host area stays fully transparent for input passthrough");
        if (hint) Require(bounds.Width <= DockPlacement.HintThickness || bounds.Height <= DockPlacement.HintThickness, "Only the thin hint is visible at rest");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
