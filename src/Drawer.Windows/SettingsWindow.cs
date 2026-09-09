using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Drawer.Windows;

public sealed class SettingsWindow : Window
{
    public SettingsWindow(App app)
    {
        Title = "drawer · 偏好设置"; Width = 780; MaxWidth = SystemParameters.WorkArea.Width - 32; SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 40);
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SettingsDesign.Setup(this);
        var tabs = new TabControl(); SettingsDesign.SetContent(this, "偏好设置", "", tabs);
        var basic = new StackPanel { Margin = new Thickness(0) };
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var add = new Button { Style = SettingsDesign.Style(this, "PrimaryButton"), Content = "+  新增抽屉", Padding = new Thickness(12, 7, 12, 7) };
        DockPanel.SetDock(add, Dock.Right); heading.Children.Add(add);
        var count = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }; heading.Children.Add(count); basic.Children.Add(heading);
        var list = new StackPanel();
        basic.Children.Add(new Border { Background = Brushes.White, BorderBrush = SettingsDesign.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            Child = new ScrollViewer { Content = list, MaxHeight = 410, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed }; basic.Children.Add(status);
        void ShowStatus(string? message)
        {
            status.Text = message; status.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }
        void Configure(DrawerHost host)
        {
            app.IsShortcutDialogOpen = true;
            try { new DrawerOptionsWindow(app, host) { Owner = this }.ShowDialog(); }
            finally { app.RecordShortcut(null); app.IsShortcutDialogOpen = false; }
            Refresh();
        }
        void Remove(DrawerHost host)
        {
            if (MessageBox.Show(this, $"删除抽屉“{host.Label}”及其中的 {host.Session.State.Items.Count} 个对象？\n\n文字和截图将从抽屉中移除，源文件不会删除。\n删除抽屉不能撤销。", "删除抽屉", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            ShowStatus(app.ChangeDrawers(w => w.Remove(host.Id), out string? error) ? null : error);
        }
        void ConfigureShortcut(DrawerHost host)
        {
            app.IsShortcutDialogOpen = true;
            try { new ShortcutWindow(app, host) { Owner = this }.ShowDialog(); }
            finally { app.RecordShortcut(null); app.IsShortcutDialogOpen = false; }
            Refresh();
        }
        void Refresh()
        {
            list.Children.Clear(); count.Text = $"我的抽屉   {app.Drawers.Count} / 5";
            add.IsEnabled = app.Drawers.Count < DrawerWorkspace.MaximumDrawers;
            add.ToolTip = add.IsEnabled ? "新增一个独立抽屉" : "最多支持 5 个抽屉";
            foreach (var host in app.Drawers)
            {
                var row = new Grid { Margin = new Thickness(18, 14, 16, 14) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(172) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
                identity.Children.Add(new TextBlock { Text = host.Label, ToolTip = host.Label, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                var preferences = host.Session.State.Preferences;
                string edge = preferences.Edge switch { DockEdge.Left => "左侧停靠", DockEdge.Right => "右侧停靠", _ => "顶部停靠" };
                string dimensions = preferences.WidthPercent is { } widthPercent && preferences.HeightPercent is { } heightPercent
                    ? $"{widthPercent:0}% × {heightPercent:0}%" : $"{preferences.Width:0} × {preferences.Height:0}";
                var screen = host.Window.ScreenSize;
                var fitted = DrawerSizing.Resolve(preferences, screen.Width, screen.Height);
                identity.Children.Add(new TextBlock { Text = $"{edge} · {dimensions}", ToolTip = $"配置尺寸（宽 × 高）：{dimensions}\n当前展开尺寸：{fitted.Width:0} × {fitted.Height:0}\n尺寸随 Windows 显示缩放调整。", Foreground = SettingsDesign.Muted, FontSize = 12, Margin = new Thickness(0, 5, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
                string themeName = DrawerPalette.Name(preferences.Theme);
                identity.Children.Add(new TextBlock { Text = $"{themeName} · {host.Session.State.Items.Count} 个对象", Foreground = SettingsDesign.Muted, FontSize = 12, Margin = new Thickness(0, 3, 0, 0) });
                var identityRow = new DockPanel();
                var miniature = new Grid { Width = 24, Height = 22 };
                miniature.Children.Add(new Border { BorderBrush = DrawerPalette.For(host.Session.State.Preferences.Theme).Ink, BorderThickness = new Thickness(1.3), CornerRadius = new CornerRadius(4) });
                miniature.Children.Add(new Border { Background = DrawerPalette.For(host.Session.State.Preferences.Theme).Ink, Width = 8, Height = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0), CornerRadius = new CornerRadius(1) });
                var badge = new Border { Background = DrawerPalette.For(host.Session.State.Preferences.Theme).Surface, Child = miniature, Width = 46, Height = 46, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 14, 0) };
                DockPanel.SetDock(badge, Dock.Left); identityRow.Children.Add(badge); identityRow.Children.Add(identity); row.Children.Add(identityRow);
                var hotkey = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) }; Grid.SetColumn(hotkey, 1); row.Children.Add(hotkey);
                hotkey.Children.Add(new TextBlock { Text = "呼出快捷键", FontSize = 11, Foreground = SettingsDesign.Muted, Margin = new Thickness(0, 0, 0, 5) });
                var shortcut = new Button { Content = preferences.OpenShortcut?.ToString() ?? "+ 设置快捷键", Padding = new Thickness(10, 7, 10, 7), HorizontalContentAlignment = HorizontalAlignment.Left, ToolTip = app.ShortcutStatus(host) + "\n点击修改呼出快捷键" };
                AutomationProperties.SetName(shortcut, "呼出快捷键 · " + host.Label);
                shortcut.Click += (_, _) => ConfigureShortcut(host); hotkey.Children.Add(shortcut);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(actions, 2); row.Children.Add(actions);
                actions.Children.Add(IconButton("打开抽屉", host.Label, "M 10,3 L 3,3 L 3,21 L 21,21 L 21,14 M 14,3 L 21,3 L 21,10 M 21,3 L 10,14", () => host.Window.OpenFocused()));
                actions.Children.Add(IconButton("抽屉配置", host.Label, "M 3,5 L 7,5 M 11,5 L 21,5 M 3,12 L 13,12 M 17,12 L 21,12 M 3,19 L 7,19 M 11,19 L 21,19 M 11,5 A 2,2 0 1 1 7,5 A 2,2 0 1 1 11,5 M 17,12 A 2,2 0 1 1 13,12 A 2,2 0 1 1 17,12 M 11,19 A 2,2 0 1 1 7,19 A 2,2 0 1 1 11,19", () => Configure(host)));
                var remove = IconButton("删除抽屉", host.Label, "M 3,6 L 21,6 M 8,6 L 8,3 L 16,3 L 16,6 M 5,6 L 6,21 L 18,21 L 19,6 M 10,10 L 10,17 M 14,10 L 14,17", () => Remove(host));
                remove.IsEnabled = app.Drawers.Count > 1;
                if (!remove.IsEnabled) { remove.ToolTip = "至少保留一个抽屉"; ToolTipService.SetShowOnDisabled(remove, true); }
                actions.Children.Add(remove);
                list.Children.Add(new Border { Child = row, BorderBrush = SettingsDesign.Line, BorderThickness = new Thickness(0, 0, 0, host == app.Drawers[^1] ? 0 : 1) });
            }
        }
        add.Click += (_, _) =>
        {
            var display = System.Windows.Forms.Screen.PrimaryScreen!.Bounds; var dpi = VisualTreeHelper.GetDpi(app.Edge);
            DockPlacement? available = null;
            foreach (var edge in Enum.GetValues<DockEdge>())
            {
                available = new DockPlacement(edge, .5).AvoidOverlap(app.Drawers.Select(d => DockPlacement.From(d.Session.State.Preferences)), display.Width / dpi.DpiScaleX, display.Height / dpi.DpiScaleY);
                if (available is not null) break;
            }
            if (available is null) { ShowStatus("屏幕边缘空间不足，请先调整现有抽屉位置。"); return; }
            ShowStatus(app.ChangeDrawers(w => w.Add(available.Value), out string? error) ? null : error);
        };
        var placement = new DockPanel();
        var position = new Button { Content = "调整位置  ↗", Padding = new Thickness(14, 9, 14, 9), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(position, Dock.Right); placement.Children.Add(position);
        var placementText = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        var placementTitle = SettingsDesign.Text("屏幕停靠", 14); placementTitle.FontWeight = FontWeights.SemiBold; placementText.Children.Add(placementTitle);
        var placementDescription = SettingsDesign.Text("沿屏幕边缘，为抽屉找到合适的位置。", 12, true); placementDescription.Margin = new Thickness(0, 6, 0, 0); placementText.Children.Add(placementDescription); placement.Children.Add(placementText);
        position.Click += (_, _) => app.BeginPositionEdit();
        var placementCard = SettingsDesign.Card(placement, 20); placementCard.Margin = new Thickness(0, 22, 0, 0); basic.Children.Add(placementCard);
        var startup = new StartupRegistration();
        var startupRow = new DockPanel();
        var startupSwitch = new CheckBox { Style = SettingsDesign.Style(this, "SettingSwitch"), VerticalAlignment = VerticalAlignment.Center, ToolTip = "登录 Windows 后自动启动 drawer" };
        AutomationProperties.SetName(startupSwitch, "开机自启"); DockPanel.SetDock(startupSwitch, Dock.Right); startupRow.Children.Add(startupSwitch);
        var startupText = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        var startupTitle = SettingsDesign.Text("开机自启", 14); startupTitle.FontWeight = FontWeights.SemiBold; startupText.Children.Add(startupTitle);
        var startupNote = SettingsDesign.Text("登录 Windows 后在后台启动，抽屉保持收起。", 12, true); startupNote.Margin = new Thickness(0, 6, 0, 0); startupText.Children.Add(startupNote);
        var startupError = SettingsDesign.Text("", 12); startupError.Foreground = Brushes.Firebrick; startupError.Visibility = Visibility.Collapsed; startupText.Children.Add(startupError);
        startupRow.Children.Add(startupText);
        var startupCard = SettingsDesign.Card(startupRow, 20); startupCard.Margin = new Thickness(0, 12, 0, 0); basic.Children.Add(startupCard);
        void RefreshStartup()
        {
            try { startupSwitch.IsChecked = startup.IsEnabled; startupSwitch.IsEnabled = true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { startupSwitch.IsEnabled = false; startupError.Text = "无法读取自启设置，请检查当前用户权限。"; startupError.Visibility = Visibility.Visible; }
        }
        startupSwitch.Click += (_, _) =>
        {
            try { startup.SetEnabled(startupSwitch.IsChecked == true); startupError.Visibility = Visibility.Collapsed; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
            { startupError.Text = "设置失败：" + ex.Message; startupError.Visibility = Visibility.Visible; }
            RefreshStartup();
        };
        Activated += (_, _) => RefreshStartup(); RefreshStartup();
        app.DrawersChanged += Refresh; Activated += (_, _) => Refresh(); Closed += (_, _) => app.DrawersChanged -= Refresh; Refresh();
        tabs.Items.Add(new TabItem { Header = "基础", Content = new ScrollViewer { Content = basic, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        var about = new StackPanel();
        var introduction = new Grid { Margin = new Thickness(4, 10, 4, 34) };
        introduction.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        introduction.ColumnDefinitions.Add(new ColumnDefinition());
        introduction.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var logo = new System.Windows.Controls.Image { Source = AppIcon.AboutIcon, Width = 112, Height = 112, Margin = new Thickness(0, 0, 28, 0), VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality); introduction.Children.Add(logo);
        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(identity, 1); introduction.Children.Add(identity);
        var wordmark = new System.Windows.Controls.Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/drawer;component/Assets/DrawerWordmark.png")),
            // The source has 101 transparent pixels before the lettering (2040 pixels wide).
            Width = 240, Margin = new Thickness(-12, 0, 0, 0), Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left
        };
        AutomationProperties.SetName(wordmark, "Drawer");
        RenderOptions.SetBitmapScalingMode(wordmark, BitmapScalingMode.HighQuality); identity.Children.Add(wordmark);
        var tagline = SettingsDesign.Text("只希望能提供一些微小的帮助", 13);
        tagline.Foreground = new SolidColorBrush(Color.FromRgb(153, 160, 171));
        tagline.Margin = new Thickness(0, 8, 0, 0); identity.Children.Add(tagline);
        var version = new Border { Background = new SolidColorBrush(Color.FromRgb(240, 243, 248)), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 5, 12, 5), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 8, 0, 0), Child = SettingsDesign.Text("v1.3", 12, true) };
        Grid.SetColumn(version, 2); introduction.Children.Add(version); about.Children.Add(introduction);
        about.Children.Add(new Border { Height = 1, Background = SettingsDesign.Line });
        var footer = new DockPanel { Margin = new Thickness(0, 22, 0, 0), LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var project = new Button { Content = "项目链接", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 12, 0), ToolTip = "https://github.com/XXXXXie1997/drawer-win" };
        project.Click += (_, _) =>
        {
            try { FileReferenceService.Open("https://github.com/XXXXXie1997/drawer-win"); }
            catch { MessageBox.Show(this, "无法打开项目链接，请检查默认浏览器设置。", "drawer"); }
        };
        var folder = new Button { Content = "打开本地数据目录", Padding = new Thickness(12, 8, 12, 8) }; folder.Click += (_, _) => FileReferenceService.Open(app.Store.Root);
        buttons.Children.Add(project); buttons.Children.Add(folder); DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons);
        var copyright = SettingsDesign.Text("© 2026 xie-bro", 12, true); copyright.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(copyright); about.Children.Add(footer);
        tabs.Items.Add(new TabItem { Header = "关于", Content = new ScrollViewer { Content = SettingsDesign.Card(about, 28), VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.SelectedIndex = app.SettingsPage == "关于" ? 1 : 0;
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != tabs) return;
            app.SettingsPage = tabs.SelectedIndex == 1 ? "关于" : "基础"; Title = "drawer · " + app.SettingsPage; app.Save();
        };
    }
    private Button IconButton(string action, string label, string geometry, Action clicked)
    {
        var icon = new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry),  StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 17, Height = 17, Stretch = Stretch.Uniform };
        var button = new Button { Content = icon, ToolTip = action, Width = 36, Height = 36, Margin = new Thickness(4, 0, 0, 0),
            Style = SettingsDesign.Style(this, action == "删除抽屉" ? "DangerIconButton" : "IconButton") };
        icon.SetBinding(System.Windows.Shapes.Path.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
        AutomationProperties.SetName(button, action + " · " + label);
        button.IsEnabledChanged += (_, _) => icon.Opacity = button.IsEnabled ? 1 : .3;
        button.Click += (_, _) => clicked(); return button;
    }
}
