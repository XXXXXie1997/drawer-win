using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace Drawer.Windows;

public static class SettingsDesign
{
    public static Brush Ink { get; } = Brush("#242933");
    public static Brush Muted { get; } = Brush("#79818E");
    public static Brush Line { get; } = Brush("#E5E8EE");
    public static Brush Backdrop { get; } = Brush("#F5F6F8");
    private static Brush Brush(string hex) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); return brush; }
    public static Style Style(Window window, string key) => (Style)window.FindResource(key);
    public static TextBlock Text(string text, double size = 13, bool muted = false) => new()
    { Text = text, FontSize = size, Foreground = muted ? Muted : Ink, TextWrapping = TextWrapping.Wrap };
    public static Border Card(UIElement child, double padding = 22) => new()
    { Child = child, Padding = new Thickness(padding), Background = Brushes.White, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14) };

    public static void Setup(Window window)
    {
        window.Icon = AppIcon.WindowIcon;
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/drawer;component/SettingsStyles.xaml", UriKind.Relative) });
        window.WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(14) });
        window.Background = Backdrop; window.Foreground = Ink; window.FontSize = 13;
        window.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        window.UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
    }
    public static TextBlock SetContent(Window window, string title, string subtitle, UIElement content)
    {
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var caption = new Grid { Height = 52, Background = Brushes.Transparent, Margin = new Thickness(20, 4, 12, 0) };
        var brand = Text("Drawer - 随放随取", 15); brand.FontWeight = FontWeights.SemiBold; brand.VerticalAlignment = VerticalAlignment.Center; brand.IsHitTestVisible = false; caption.Children.Add(brand);
        var closeIcon = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 1,1 L 11,11 M 11,1 L 1,11"), Width = 12, Height = 12, StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        var close = new Button { Content = closeIcon, Width = 32, Height = 32, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, Style = Style(window, "IconButton"), ToolTip = "关闭" };
        closeIcon.SetBinding(System.Windows.Shapes.Path.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = close });
        System.Windows.Automation.AutomationProperties.SetName(close, "关闭窗口");
        close.Click += (_, _) => window.Close(); caption.Children.Add(close);
        caption.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == caption && e.LeftButton == MouseButtonState.Pressed) window.DragMove(); };
        root.Children.Add(caption);
        var body = new Grid { Margin = new Thickness(30, 8, 30, 28) }; Grid.SetRow(body, 1); root.Children.Add(body);
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
        var titleText = Text(title, 27); titleText.FontWeight = FontWeights.SemiBold; titleText.TextWrapping = TextWrapping.NoWrap; titleText.TextTrimming = TextTrimming.CharacterEllipsis; heading.Children.Add(titleText);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var description = Text(subtitle, 13, true); description.Margin = new Thickness(0, 8, 0, 0); heading.Children.Add(description);
        }
        body.Children.Add(heading);
        Grid.SetRow(content, 1); body.Children.Add(content);
        window.Content = new Border { Child = root, Background = Backdrop, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14) };
        return titleText;
    }
}
