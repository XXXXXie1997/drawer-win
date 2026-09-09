using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Drawer.Windows;

public sealed class ShortcutWindow : Window
{
    private readonly App app;
    private readonly TextBox display;
    private readonly TextBlock status;
    private ShortcutBinding? draft;
    private bool recording;

    public ShortcutWindow(App app, DrawerHost host, DrawerCommand? command = null)
    {
        this.app = app;
        draft = command is { } action ? host.Session.State.Preferences.CommandShortcuts.TryGetValue(action, out var local) ? local : null : host.Session.State.Preferences.OpenShortcut;
        status = new TextBlock { Text = command is null ? app.ShortcutStatus(host) : draft?.ToString() ?? "未设置", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Foreground = SettingsDesign.Muted };
        Title = (command is { } c ? DrawerCommands.Label(c) : "呼出快捷键") + " · " + host.Label;
        Width = 490; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SettingsDesign.Setup(this);
        var panel = new StackPanel(); SettingsDesign.SetContent(this, "设置快捷键", host.Label + " · " + (command is { } labelCommand ? DrawerCommands.Label(labelCommand) : "全局呼出"), panel);
        panel.Children.Add(new TextBlock { Text = command is null ? "在其他应用中也能直接呼出此抽屉。" : "仅在此抽屉的画布中生效，文字编辑时不触发。", Margin = new Thickness(0, 0, 0, 14) });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        display = new TextBox { IsReadOnly = true, Width = 260, Padding = new Thickness(10, 8, 10, 8) };
        InputMethod.SetIsInputMethodEnabled(display, false);
        row.Children.Add(display);
        var record = new Button { Content = "录制", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(10, 0, 0, 0) };
        record.Click += (_, _) =>
        {
            recording = true; app.RecordShortcut(Accept);
            display.Text = "请按组合键…"; status.Foreground = SettingsDesign.Muted;
            status.Text = "按 Esc 取消录制；录制后点击保存。"; display.Focus();
        };
        row.Children.Add(record); panel.Children.Add(row);
        panel.Children.Add(new TextBlock { Text = "Ctrl 或 Alt + 字母、数字、F1–F11；也支持 Ctrl+Alt+空格。基础编辑快捷键保持固定。", TextWrapping = TextWrapping.Wrap, Foreground = SettingsDesign.Muted, Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(status);
        var actions = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        Button Add(string label, Action action)
        {
            var button = new Button { Content = label, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 8) };
            button.Click += (_, _) => action(); actions.Children.Add(button); return button;
        }
        var suggested = command is { } suggestedCommand ? DrawerCommands.Suggest(suggestedCommand) : new ShortcutBinding(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x31 + app.Drawers.IndexOf(host));
        Add("使用 " + suggested, () => { StopRecording(); draft = suggested; ShowDraft(); Pending(); });
        Add("清除", () => { StopRecording(); draft = null; ShowDraft(); Pending(); });
        panel.Children.Add(actions);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => Close();
        var save = new Button { Style = SettingsDesign.Style(this, "PrimaryButton"), Content = "保存", Padding = new Thickness(16, 8, 16, 8), IsDefault = true };
        save.Click += (_, _) =>
        {
            StopRecording();
            string? error;
            bool saved = command is { } target ? app.SetCommandShortcut(host.Id, target, draft, out error) : app.SetShortcut(host.Id, draft, out error);
            if (saved) Close();
            else { status.Foreground = Brushes.Firebrick; status.Text = error; }
        };
        footer.Children.Add(cancel); footer.Children.Add(save); panel.Children.Add(footer);
        display.PreviewKeyDown += (_, e) =>
        {
            if (!recording) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
            if (key == Key.Tab) { StopRecording(); return; }
            e.Handled = true;
            if (key == Key.Escape) { StopRecording(); status.Text = "已取消录制。"; return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) { Invalid(); return; }
            Accept(new((ShortcutModifiers)(int)Keyboard.Modifiers, KeyInterop.VirtualKeyFromKey(key)));
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (recording) { StopRecording(); status.Text = "已取消录制。"; }
            else Close();
            e.Handled = true;
        };
        display.LostKeyboardFocus += (_, _) => StopRecording();
        Deactivated += (_, _) => StopRecording();
        Closed += (_, _) => StopRecording();
        ShowDraft();
    }
    private void Accept(ShortcutBinding binding)
    {
        if (!recording) return;
        if (!binding.IsValid) { Invalid(); return; }
        draft = binding; StopRecording(); Pending();
    }
    private void Invalid() { status.Foreground = Brushes.Firebrick; status.Text = "此组合不可用，请换一个，例如 Ctrl+Alt+1。"; }
    private void Pending() { status.Foreground = SettingsDesign.Muted; status.Text = "尚未生效，点击保存应用修改。"; }
    private void StopRecording() { recording = false; app.RecordShortcut(null); ShowDraft(); }
    private void ShowDraft() => display.Text = draft?.ToString() ?? "未设置";
}
