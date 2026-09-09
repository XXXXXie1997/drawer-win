using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace Drawer.Windows;

public sealed class PositionEditorWindow : Window
{
    private readonly App app;
    private readonly Dictionary<Guid, DockPlacement> placements;
    private readonly Dictionary<Guid, Border> handles = [];
    private readonly Canvas surface = new();
    private readonly TextBlock status, error;
    private readonly Border toolbar;
    private Guid activeId;
    private bool dragging, moved, finishing;
    private Point press;
    private double grabOffset;
    public PositionEditorWindow(App app)
    {
        this.app = app;
        Icon = AppIcon.WindowIcon;
        placements = app.Drawers.ToDictionary(d => d.Id, d => DockPlacement.From(d.Session.State.Preferences));
        activeId = app.Drawers[0].Id;
        Title = "drawer · 调整停靠位置"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; ShowInTaskbar = false; Topmost = true;
        Background = new SolidColorBrush(Color.FromArgb(65, 0, 0, 0)); FontFamily = new FontFamily("Segoe UI"); UseLayoutRounding = true;
        var root = new Grid { ClipToBounds = true }; Content = root; root.Children.Add(surface);
        foreach (var host in app.Drawers)
        {
            var handle = new Border
            {
                Background = Brushes.Black, Cursor = Cursors.SizeAll, ToolTip = host.Label,
                BorderBrush = new SolidColorBrush(Color.FromRgb(125, 180, 255)), BorderThickness = new Thickness(1),
                Child = new HandleLabel()
            };
            handles.Add(host.Id, handle); surface.Children.Add(handle);
            var palette = DrawerPalette.For(host.Session.State.Preferences.Theme);
            handle.Background = palette.Shell; ((HandleLabel)handle.Child).Foreground = palette.Ink;
            handle.MouseLeftButtonDown += (_, e) =>
            {
                if (finishing) return;
                activeId = host.Id; press = e.GetPosition(surface);
                var bounds = placements[activeId].HandleBounds(Width, Height);
                grabOffset = placements[activeId].Edge == DockEdge.Top ? press.X - bounds.X - bounds.Width / 2 : press.Y - bounds.Y - bounds.Height / 2;
                dragging = handle.CaptureMouse(); moved = false; handle.RenderTransform = Transform.Identity;
                e.Handled = true; RenderPlacements();
            };
            handle.MouseMove += (_, e) =>
            {
                if (!dragging || finishing || activeId != host.Id) return;
                var point = e.GetPosition(surface);
                if (!moved && Math.Abs(point.X - press.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - press.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                moved = true;
                var desired = placements[activeId].Drag(point.X, point.Y, Width, Height, grabOffset);
                var next = desired.AvoidOverlap(placements.Where(p => p.Key != activeId).Select(p => p.Value), Width, Height);
                if (next is { } available)
                {
                    if (available.Edge != placements[activeId].Edge) grabOffset = 0;
                    placements[activeId] = available; RenderPlacements();
                }
                e.Handled = true;
            };
            handle.MouseLeftButtonUp += (_, e) => { dragging = false; handle.ReleaseMouseCapture(); e.Handled = true; };
            handle.LostMouseCapture += (_, _) => dragging = false;
        }
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "调整停靠位置", Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "拖动任意把手，沿顶部、左侧和右侧移动。\n把手会自动避让，确认后保存全部位置。", Foreground = Brushes.LightGray, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
        status = new TextBlock { Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0, 10, 0, 0) }; panel.Children.Add(status);
        error = new TextBlock { Foreground = Brushes.Salmon, TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) }; panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = "取消 · Esc", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 10, 0) };
        var confirm = new Button { Content = "确认修改", Padding = new Thickness(16, 8, 16, 8), IsDefault = true };
        cancel.Click += (_, _) => Finish(false); confirm.Click += (_, _) => Finish(true);
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); panel.Children.Add(buttons);
        toolbar = new Border { Child = panel, Background = new SolidColorBrush(Color.FromRgb(30, 32, 38)), CornerRadius = new CornerRadius(16), Padding = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(24, 24, 24, 40) };
        root.Children.Add(toolbar);
        SourceInitialized += (_, _) =>
        {
            Native.SetNonActivating(this, false);
            var display = System.Windows.Forms.Screen.PrimaryScreen!.Bounds; var dpi = VisualTreeHelper.GetDpi(this);
            Left = display.Left / dpi.DpiScaleX; Top = display.Top / dpi.DpiScaleY;
            Width = display.Width / dpi.DpiScaleX; Height = display.Height / dpi.DpiScaleY;
            RenderPlacements();
        };
        Loaded += (_, _) =>
        {
            Activate(); confirm.Focus();
            foreach (var handle in handles.Values)
            {
                var scale = new ScaleTransform(1, 1); handle.RenderTransform = scale;
                var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .35 } };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation); scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
            }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Finish(false); e.Handled = true; }
            else if (dragging && e.Key == Key.Enter) e.Handled = true;
        };
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Closed += (_, _) => { SystemEvents.DisplaySettingsChanged -= DisplayChanged; foreach (var handle in handles.Values) handle.ReleaseMouseCapture(); };
    }
    private void RenderPlacements()
    {
        foreach (var (id, handle) in handles)
        {
            var placement = placements[id]; var bounds = placement.HandleBounds(Width, Height);
            Canvas.SetLeft(handle, bounds.X); Canvas.SetTop(handle, bounds.Y); handle.Width = bounds.Width; handle.Height = bounds.Height;
            handle.CornerRadius = placement.Edge switch { DockEdge.Left => new(0, 11, 11, 0), DockEdge.Right => new(11, 0, 0, 11), _ => new(0, 0, 11, 11) };
            handle.RenderTransformOrigin = placement.Edge switch { DockEdge.Left => new(0, .5), DockEdge.Right => new(1, .5), _ => new(.5, 0) };
            ((HandleLabel)handle.Child).Update(app.Drawers.Single(d => d.Id == id).Label, placement.Edge);
        }
        var active = placements[activeId];
        string edge = active.Edge switch { DockEdge.Left => "左侧", DockEdge.Right => "右侧", _ => "顶部" };
        status.Text = $"{app.Drawers.Single(d => d.Id == activeId).Label} · {edge} · {active.Position:P0}";
    }
    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { if (!finishing) Finish(false); });
    private void Finish(bool commit)
    {
        if (finishing) return;
        dragging = false; foreach (var handle in handles.Values) handle.ReleaseMouseCapture();
        if (commit)
        {
            try { app.SavePlacements(placements); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { error.Text = "位置保存失败，请重试，或取消以保留原位置。"; error.Visibility = Visibility.Visible; return; }
        }
        finishing = true; toolbar.IsEnabled = false;
        foreach (var handle in handles.Values)
        {
            var scale = new ScaleTransform(1, 1); handle.RenderTransform = scale;
            var shrink = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink); scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)); fade.Completed += (_, _) => Close(); BeginAnimation(OpacityProperty, fade);
    }
}
