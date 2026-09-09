namespace Drawer.Core;

public enum DrawerTheme { Dark, Light, Midnight, Forest, Plum, Rose, Sand, Ocean }
public enum DrawerCommand { FitAll, ReturnToPoint, ResetZoom, OpenSelected, RevealSelected, OpenSettings, ClearDrawer, ExitApp }

public static class DrawerCommands
{
    public static string Label(DrawerCommand command) => command switch
    {
        DrawerCommand.FitAll => "显示全部", DrawerCommand.ReturnToPoint => "回到落点",
        DrawerCommand.ResetZoom => "恢复 100% 缩放", DrawerCommand.OpenSelected => "打开所选文件",
        DrawerCommand.RevealSelected => "在文件管理器中显示", DrawerCommand.OpenSettings => "偏好设置",
        DrawerCommand.ClearDrawer => "清空抽屉", _ => "退出 drawer"
    };
    public static ShortcutBinding Suggest(DrawerCommand command) => command switch
    {
        DrawerCommand.ResetZoom => new(ShortcutModifiers.Control, 0x30),
        DrawerCommand.OpenSelected => new(ShortcutModifiers.Control, 0x4F),
        _ => new(ShortcutModifiers.Control | ShortcutModifiers.Shift, command switch
        {
            DrawerCommand.FitAll => 0x46, DrawerCommand.ReturnToPoint => 0x4C,
            DrawerCommand.RevealSelected => 0x4F, DrawerCommand.OpenSettings => 0x50,
            DrawerCommand.ClearDrawer => 0x4B, _ => 0x51
        })
    };
    public static void Validate(Preferences preferences)
    {
        if (preferences.WidthPercent.HasValue != preferences.HeightPercent.HasValue)
            throw new InvalidDataException("抽屉尺寸百分比不完整");
        if (preferences.WidthPercent is { } width && preferences.HeightPercent is { } height)
            DrawerSizing.SetPercentages(preferences, width, height);
        if (!Enum.IsDefined(preferences.Theme) || preferences.CommandShortcuts is null ||
            preferences.CommandShortcuts.Any(p => !Enum.IsDefined(p.Key) || !p.Value.IsValid))
            throw new InvalidDataException("主题或操作快捷键无效");
        if (preferences.CommandShortcuts.Values.Distinct().Count() != preferences.CommandShortcuts.Count)
            throw new InvalidDataException("同一抽屉的操作快捷键不能重复");
    }
    public static void SetSize(Preferences preferences, double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width is < DrawerSizing.Minimum or > DrawerSizing.Maximum || height is < DrawerSizing.Minimum or > DrawerSizing.Maximum)
            throw new InvalidDataException($"宽度和高度均应为 {DrawerSizing.Minimum}–{DrawerSizing.Maximum}，展开时自动适配屏幕。");
        preferences.Width = width; preferences.Height = height;
        preferences.WidthPercent = preferences.HeightPercent = null;
    }
}
