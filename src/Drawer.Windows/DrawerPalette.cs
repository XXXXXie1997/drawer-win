using System.Windows.Media;

namespace Drawer.Windows;

public sealed record DrawerPalette(Brush Shell, Brush Surface, Brush Border, Brush Ink, Brush Muted, Brush Accent, Brush Dot, Brush Card, Brush Selection, Brush Editor)
{
    private static Brush Hex(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); brush.Freeze(); return brush;
    }
    private static readonly DrawerPalette Dark = new(Hex("#000000"), Hex("#191B20"), Hex("#2B2D33"), Hex("#E1E5ED"), Hex("#949DAE"), Hex("#78ACFF"), Hex("#373B42"), Hex("#22A0B1C9"), Hex("#2578ACFF"), Hex("#1D2027"));
    private static readonly DrawerPalette Light = new(Hex("#E9EDF3"), Hex("#F8FAFC"), Hex("#C8D1DE"), Hex("#202C3E"), Hex("#65748B"), Hex("#2466CF"), Hex("#CCD5E0"), Hex("#182466CF"), Hex("#282466CF"), Hex("#FFFFFF"));
    private static readonly DrawerPalette Midnight = new(Hex("#0B1427"), Hex("#132139"), Hex("#2C4062"), Hex("#E0ECFF"), Hex("#9EAFCE"), Hex("#9EB9FF"), Hex("#30425D"), Hex("#229EB9FF"), Hex("#309EB9FF"), Hex("#1A2B47"));
    private static readonly DrawerPalette Forest = new(Hex("#16382C"), Hex("#1D3028"), Hex("#395748"), Hex("#E8F4E9"), Hex("#A6C4AD"), Hex("#91D5A3"), Hex("#385044"), Hex("#2291D5A3"), Hex("#3091D5A3"), Hex("#283E32"));
    private static readonly DrawerPalette Plum = new(Hex("#392443"), Hex("#2D2234"), Hex("#584360"), Hex("#F3EAF8"), Hex("#C2AACD"), Hex("#D2AAF2"), Hex("#4A3854"), Hex("#22D2AAF2"), Hex("#30D2AAF2"), Hex("#3C2D46"));
    private static readonly DrawerPalette Rose = new(Hex("#EAC5CE"), Hex("#FFF2F4"), Hex("#D8ACB8"), Hex("#512B38"), Hex("#86606D"), Hex("#AE3E63"), Hex("#E4C8D0"), Hex("#18AE3E63"), Hex("#28AE3E63"), Hex("#FFF9FA"));
    private static readonly DrawerPalette Sand = new(Hex("#DAC9A7"), Hex("#FAF4E7"), Hex("#C8B58D"), Hex("#443821"), Hex("#7B6C50"), Hex("#95621F"), Hex("#DED1B7"), Hex("#1895621F"), Hex("#2895621F"), Hex("#FFFCF5"));
    private static readonly DrawerPalette Ocean = new(Hex("#12424B"), Hex("#18353C"), Hex("#38606A"), Hex("#E4F5F5"), Hex("#A0C6CC"), Hex("#7EDADD"), Hex("#33535B"), Hex("#227EDADD"), Hex("#307EDADD"), Hex("#234650"));
    public static string Name(DrawerTheme theme) => theme switch
    {
        DrawerTheme.Light => "浅色", DrawerTheme.Midnight => "午夜蓝", DrawerTheme.Forest => "森林绿",
        DrawerTheme.Plum => "烟紫", DrawerTheme.Rose => "玫瑰粉", DrawerTheme.Sand => "暖沙", DrawerTheme.Ocean => "深海青", _ => "深色"
    };
    public static DrawerPalette For(DrawerTheme theme) => theme switch
    {
        DrawerTheme.Light => Light, DrawerTheme.Midnight => Midnight, DrawerTheme.Forest => Forest,
        DrawerTheme.Plum => Plum, DrawerTheme.Rose => Rose, DrawerTheme.Sand => Sand, DrawerTheme.Ocean => Ocean, _ => Dark
    };
}
