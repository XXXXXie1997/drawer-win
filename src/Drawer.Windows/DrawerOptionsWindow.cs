using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Drawer.Windows;

public sealed class DrawerOptionsWindow : Window
{
    public DrawerOptionsWindow(App app, DrawerHost host)
    {
        Title = "抽屉设置 · " + host.Label; Width = 480; Height = 600; MaxWidth = SystemParameters.WorkArea.Width - 32; MaxHeight = SystemParameters.WorkArea.Height - 32;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        SettingsDesign.Setup(this);
        var layout = new Grid(); layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tabs = new TabControl(); layout.Children.Add(tabs);
        var heading = SettingsDesign.SetContent(this, "抽屉 · " + host.Label, "", layout);
        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) }; Grid.SetRow(footer, 1); layout.Children.Add(footer);
        var save = new Button { Content = "保存", MinWidth = 88, Style = SettingsDesign.Style(this, "PrimaryButton") }; DockPanel.SetDock(save, Dock.Right); footer.Children.Add(save);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) }; footer.Children.Add(status);
        var general = new StackPanel { Margin = new Thickness(0) };
        general.Children.Add(new TextBlock { Text = "抽屉标识", FontWeight = FontWeights.SemiBold });
        var nameRow = new DockPanel { Width = 240, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        var name = new TextBox { Text = host.Label, Padding = new Thickness(8) }; nameRow.Children.Add(name); general.Children.Add(nameRow);
        general.Children.Add(new TextBlock { Text = $"标识不可重复，最多 {DrawerWorkspace.MaximumLabelCharacters} 个字符；完整 emoji 按 1 个计算。", TextWrapping = TextWrapping.Wrap, Foreground = SettingsDesign.Muted, Margin = new Thickness(0, 10, 0, 0) });
        var nameCount = new TextBlock { Margin = new Thickness(0, 6, 0, 0), FontSize = 12 }; general.Children.Add(nameCount);
        void ValidateName()
        {
            int count = new System.Globalization.StringInfo(name.Text.Trim()).LengthInTextElements;
            nameCount.Text = $"{count} / {DrawerWorkspace.MaximumLabelCharacters}";
            try { if (name.Text != host.Label) DrawerWorkspace.NormalizeLabel(name.Text); save.IsEnabled = true; nameCount.Foreground = SettingsDesign.Muted; }
            catch (InvalidDataException ex) { save.IsEnabled = false; nameCount.Foreground = Brushes.Firebrick; nameCount.Text += " · " + ex.Message; }
        }
        name.TextChanged += (_, _) => ValidateName(); ValidateName();
        general.Children.Add(new TextBlock { Text = "框选方式", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 24, 0, 10) });
        var selection = new ComboBox { Width = 240, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = new[] { "相交即选中", "完全包含才选中" }, SelectedIndex = host.Session.State.Preferences.SelectionMustContain ? 1 : 0, Padding = new Thickness(8) }; general.Children.Add(selection);
        tabs.Items.Add(new TabItem { Header = "常规", Content = new ScrollViewer { Content = SettingsDesign.Card(general, 20), VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        var appearance = new StackPanel { Margin = new Thickness(0) };
        appearance.Children.Add(new TextBlock { Text = "展开后的抽屉大小", FontWeight = FontWeights.SemiBold });
        var screenSize = host.Window.ScreenSize;
        var percentages = DrawerSizing.Percentages(host.Session.State.Preferences, screenSize.Width, screenSize.Height);
        Slider AddSizeSlider(string label, double value)
        {
            var row = new DockPanel { Margin = new Thickness(0, 12, 0, 2) };
            var amount = SettingsDesign.Text($"{value:0}%", 13); amount.FontWeight = FontWeights.SemiBold;
            DockPanel.SetDock(amount, Dock.Right); row.Children.Add(amount); row.Children.Add(SettingsDesign.Text(label)); appearance.Children.Add(row);
            var slider = new Slider { Minimum = 10, Maximum = 90, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, SmallChange = 1, LargeChange = 10, Height = 24 };
            System.Windows.Automation.AutomationProperties.SetName(slider, label + "（屏幕百分比）");
            slider.ValueChanged += (_, _) => amount.Text = $"{slider.Value:0}%";
            appearance.Children.Add(slider); return slider;
        }
        var width = AddSizeSlider("宽度", percentages.Width);
        var height = AddSizeSlider("高度", percentages.Height);
        appearance.Children.Add(new TextBlock { Text = "宽高分别占屏幕的 10%–90%，随屏幕尺寸自动适配。", TextWrapping = TextWrapping.Wrap, Foreground = SettingsDesign.Muted, Margin = new Thickness(0, 8, 0, 18) });
        var themeRow = new Grid();
        themeRow.ColumnDefinitions.Add(new ColumnDefinition()); themeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var themeControls = new StackPanel { Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center }; themeRow.Children.Add(themeControls);
        themeControls.Children.Add(new TextBlock { Text = "抽屉主题", FontWeight = FontWeights.SemiBold });
        appearance.Children.Add(themeRow);
        var theme = new ComboBox { ItemsSource = Enum.GetValues<DrawerTheme>().Select(DrawerPalette.Name).ToArray(), SelectedIndex = (int)host.Session.State.Preferences.Theme, Margin = new Thickness(0, 12, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetName(theme, "抽屉主题"); themeControls.Children.Add(theme);
        var themePreview = new Border { Width = 150, Height = 106, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(6) };
        System.Windows.Automation.AutomationProperties.SetName(themePreview, "所选主题预览"); Grid.SetColumn(themePreview, 1); themeRow.Children.Add(themePreview);
        void Preview()
        {
            if (theme.SelectedIndex < 0) return;
            var palette = DrawerPalette.For((DrawerTheme)theme.SelectedIndex);
            themePreview.Background = palette.Surface; themePreview.BorderBrush = palette.Shell;
            var sample = new StackPanel { Margin = new Thickness(10) };
            sample.Children.Add(new TextBlock { Text = "Aa", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = palette.Ink });
            sample.Children.Add(new Border { Width = 30, Height = 4, CornerRadius = new CornerRadius(2), Background = palette.Accent, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 8) });
            var blocks = new StackPanel { Orientation = Orientation.Horizontal };
            blocks.Children.Add(new Border { Width = 29, Height = 23, CornerRadius = new CornerRadius(5), Background = palette.Card, Margin = new Thickness(0, 0, 5, 0) });
            blocks.Children.Add(new Border { Width = 40, Height = 23, CornerRadius = new CornerRadius(5), Background = palette.Selection });
            sample.Children.Add(blocks); themePreview.Child = sample;
        }
        theme.SelectionChanged += (_, _) => Preview(); Preview();
        tabs.Items.Add(new TabItem { Header = "尺寸与主题", Content = new ScrollViewer { Content = SettingsDesign.Card(appearance, 20), VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        (string, int, double, double, int) Draft() => (name.Text, selection.SelectedIndex, width.Value, height.Value, theme.SelectedIndex);
        var baseline = Draft();
        save.Click += (_, _) =>
        {
            bool saved = app.ConfigureDrawer(host.Id, p =>
            {
                p.SelectionMustContain = selection.SelectedIndex == 1;
                if (width.Value != baseline.Item3 || height.Value != baseline.Item4)
                    DrawerSizing.SetPercentages(p, width.Value, height.Value);
                p.Theme = (DrawerTheme)theme.SelectedIndex;
            }, out var error, name.Text == baseline.Item1 ? null : name.Text);
            status.Foreground = saved ? SettingsDesign.Muted : Brushes.Firebrick; status.Text = saved ? "已保存。" : error;
            if (saved) { name.Text = host.Label; baseline = Draft(); Title = "抽屉设置 · " + host.Label; heading.Text = "抽屉 · " + host.Label; }
        };
        Closing += (_, e) =>
        {
            if (!app.IsExiting && Draft() != baseline && MessageBox.Show(this, "有尚未保存的调整，确定放弃并关闭？", "未保存的调整", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                e.Cancel = true;
        };
    }
}
