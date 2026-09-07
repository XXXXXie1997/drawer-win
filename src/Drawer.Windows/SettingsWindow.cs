using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Drawer.Windows;

public sealed class SettingsWindow : Window
{
    public SettingsWindow(App app)
    {
        Title = "drawer · 偏好设置";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = SystemColors.WindowBrush;
        Foreground = SystemColors.WindowTextBrush;
        FontFamily = new FontFamily("Segoe UI");
        var tabs = new TabControl { Margin = new Thickness(24) };
        Content = tabs;
        var basic = new StackPanel { Margin = new Thickness(16, 24, 16, 24) };
        basic.Children.Add(new TextBlock { Text = "抽屉位置", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        var edges = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, edge) in new[] { ("顶部", DockEdge.Top), ("左侧", DockEdge.Left), ("右侧", DockEdge.Right) })
        {
            var radio = new RadioButton { Content = label, GroupName = "edge", IsChecked = app.Session.State.Preferences.Edge == edge, Margin = new Thickness(0, 0, 32, 0) };
            radio.Checked += (_, _) => { app.Session.State.Preferences.Edge = edge; app.Session.Save(); app.Edge.Position(false); };
            edges.Children.Add(radio);
        }
        basic.Children.Add(edges);
        basic.Children.Add(new TextBlock { Text = "框选方式", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 28, 0, 10) });
        var selection = new ComboBox { ItemsSource = new[] { "相交即选中", "完全包含才选中" }, SelectedIndex = app.Session.State.Preferences.SelectionMustContain ? 1 : 0, Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, Width = 220 };
        selection.SelectionChanged += (_, _) => { app.Session.State.Preferences.SelectionMustContain = selection.SelectedIndex == 1; app.Session.Save(); };
        basic.Children.Add(selection);
        tabs.Items.Add(new TabItem { Header = "基础", Content = basic });
        var about = new StackPanel { Margin = new Thickness(16, 22, 16, 22) };
        about.Children.Add(new TextBlock { Text = "drawer", FontSize = 48, FontWeight = FontWeights.Light });
        about.Children.Add(new TextBlock { Text = "版本 0.1.3 · 构建 4", FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 6, 0, 8) });
        about.Children.Add(new TextBlock { Text = "© 2026 drawer\n\n随手放下，需要时再拿起。\n\n内容只保存在这台设备上" });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 24, 0, 0) };
        var copy = new Button { Content = "复制版本信息", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 12, 0) };
        copy.Click += (_, _) => { try { Clipboard.SetText("drawer 0.1.3 (4) · Windows x64"); } catch { MessageBox.Show(this, "剪贴板暂时不可用，请重试。", "drawer"); } };
        var folder = new Button { Content = "显示数据文件夹", Padding = new Thickness(12, 8, 12, 8) };
        folder.Click += (_, _) => FileReferenceService.Open(app.Store.Root);
        buttons.Children.Add(copy); buttons.Children.Add(folder); about.Children.Add(buttons);
        tabs.Items.Add(new TabItem { Header = "关于", Content = about });
        tabs.SelectedIndex = app.Session.State.Preferences.SettingsPage == "关于" ? 1 : 0;
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != tabs) return;
            string page = tabs.SelectedIndex == 1 ? "关于" : "基础";
            Title = "drawer · " + page;
            app.Session.State.Preferences.SettingsPage = page;
            app.Session.Save();
        };
    }
}
