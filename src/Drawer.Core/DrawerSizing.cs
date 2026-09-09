namespace Drawer.Core;

public static class DrawerSizing
{
    public static (double Width, double Height) Percentages(Preferences p, double screenWidth, double screenHeight) =>
        (Math.Clamp(Math.Round(p.WidthPercent ?? p.Width / screenWidth * 100), 10, 90),
         Math.Clamp(Math.Round(p.HeightPercent ?? p.Height / screenHeight * 100), 10, 90));
    public static (double Width, double Height) Resolve(Preferences p, double screenWidth, double screenHeight) =>
        p.WidthPercent is { } width && p.HeightPercent is { } height
            ? (screenWidth * width / 100, screenHeight * height / 100)
            : Fit(p.Width, p.Height, screenWidth, screenHeight);
    public static void SetPercentages(Preferences p, double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width is < 10 or > 90 || height is < 10 or > 90)
            throw new InvalidDataException("宽度和高度均应为屏幕的 10%–90%。");
        p.WidthPercent = width; p.HeightPercent = height;
    }
    public const double Minimum = 160;
    public const double Maximum = 32768;

    // Preferences retain the requested logical size when a smaller display is used.
    public static (double Width, double Height) Fit(double width, double height, double screenWidth, double screenHeight)
    {
        static double Extent(double requested, double screen)
        {
            double maximum = Math.Clamp(screen, 1, Maximum);
            return Math.Clamp(requested, Math.Min(Minimum, maximum), maximum);
        }
        return (Extent(width, screenWidth), Extent(height, screenHeight));
    }
}
