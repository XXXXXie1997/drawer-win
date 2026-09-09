using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;


namespace Drawer.Windows;

public sealed class EdgeWindow : Window
{
    private readonly App app;
    private readonly Border shell;
    private readonly Border panel;
    private readonly Border hint;
    private readonly Border handleFrame;
    private readonly System.Windows.Controls.Canvas surface;
    private readonly HandleLabel handleLabel;
    private bool retired;
    private readonly Grid content;
    private readonly TextBlock notice;
    private readonly DispatcherTimer timer;
    private readonly DispatcherTimer noticeTimer;
    private DateTime outsideSince = DateTime.UtcNow;
    private bool outside;
    private bool focusedPointerEntered;
    private bool dragOver;
    private readonly record struct VisualPose(double Width, double Height, double Radius, double ContentOpacity, double LabelOpacity, double HintOpacity);
    private VisualPose pose;
    private readonly SpringValue motionWidth = new(), motionHeight = new(), motionRadius = new();
    private readonly SpringValue motionContent = new(), motionLabel = new(), motionHint = new();
    private bool animating;
    private TimeSpan? lastFrame;
    private Rect hostBounds, visibleBounds;
    private System.Drawing.Rectangle positionedScreen;
    private double positionedScale;
    private bool resizing;
    private Point resizeStart;
    private Size resizeSize;
    public DrawerMode Mode { get; private set; } = DrawerMode.Hidden;
    public bool IsTransferring { get; set; }
    public Rect VisibleBounds => visibleBounds;
    public CanvasView Canvas { get; }
    private double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;
    public Size ScreenSize
    {
        get
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            return new Size(bounds.Width / Scale, bounds.Height / Scale);
        }
    }
    public DrawerHost Host { get; }
    public EdgeWindow(App app, DrawerHost host)
    {
        this.app = app; Host = host;
        Title = "drawer";
        Icon = AppIcon.WindowIcon;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = app.InspectionMode;
        ShowActivated = false;
        Topmost = true;
        UseLayoutRounding = true;
        Focusable = true;
        AllowDrop = true;
        Canvas = new(app, this);
        content = new Grid { Margin = new Thickness(14) };
        panel = new Border
        {
            Background = new LinearGradientBrush(Color.FromRgb(29, 31, 35), Color.FromRgb(21, 23, 27), 90),
            BorderBrush = new SolidColorBrush(Color.FromRgb(43, 45, 51)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14), ClipToBounds = true, Child = Canvas
        };
        content.Children.Add(panel);
        notice = new TextBlock
        {
            Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(245, 48, 52, 60)),
            Padding = new Thickness(12, 7, 12, 7), TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, 12, 8, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false
        };
        content.Children.Add(notice);
        handleLabel = new HandleLabel();
        hint = new Border { IsHitTestVisible = false, Opacity = 1 };
        handleFrame = new Border { Child = handleLabel, IsHitTestVisible = false };
        var shellContent = new System.Windows.Controls.Canvas(); shellContent.Children.Add(content); shellContent.Children.Add(handleFrame); shellContent.Children.Add(hint);
        shell = new Border { Background = Brushes.Black, Child = shellContent, Opacity = 0, ClipToBounds = true };
        RefreshLabel();
        ApplyAppearance();
        // Keep the native window fixed throughout each animation. Alpha-zero pixels outside
        // the visible shell pass input through to the desktop in this layered window.
        Background = Brushes.Transparent;
        surface = new System.Windows.Controls.Canvas(); surface.Children.Add(shell); Content = surface;
        noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        noticeTimer.Tick += (_, _) => { notice.Visibility = Visibility.Collapsed; noticeTimer.Stop(); };
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
        timer.Tick += Tick;
        SourceInitialized += (_, _) => { Native.SetNonActivating(this, true); Position(false); timer.Start(); };
        Closed += (_, _) => { StopAnimation(); timer.Stop(); noticeTimer.Stop(); };
        Closing += (_, e) => { if (!app.IsExiting && !retired) { e.Cancel = true; Collapse(); } };
        MouseEnter += (_, _) => outside = false;
        PreviewMouseLeftButtonDown += OnPress;
        PreviewMouseMove += OnResize;
        PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (!resizing) return;
            resizing = false; ReleaseMouseCapture(); Host.Session.Save();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !IsTransferring) { Collapse(); e.Handled = true; }
        };
        Deactivated += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!app.InspectionMode && Mode == DrawerMode.Focused && !IsTransferring && !dragOver && !Native.OurAppIsForeground()) Collapse();
        }, DispatcherPriority.Background);
        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => { dragOver = false; outsideSince = DateTime.UtcNow; outside = true; };
        Drop += OnDrop;
    }
    private Rect BoundsFor(DrawerMode mode)
    {
        var display = System.Windows.Forms.Screen.PrimaryScreen!;
        double scale = Scale;
        double left = display.Bounds.Left / scale, top = display.Bounds.Top / scale;
        double width = display.Bounds.Width / scale, height = display.Bounds.Height / scale;
        var p = Host.Session.State.Preferences;
        bool expanded = mode is DrawerMode.Preview or DrawerMode.Focused;
        var fitted = DrawerSizing.Resolve(p, width, height);
        double w = expanded ? fitted.Width : p.Edge == DockEdge.Top ? 132 : mode == DrawerMode.Hidden ? DockPlacement.HintThickness : DockPlacement.HandleThickness;
        double h = expanded ? fitted.Height : p.Edge == DockEdge.Top ? mode == DrawerMode.Hidden ? DockPlacement.HintThickness : DockPlacement.HandleThickness : 132;
        var bounds = DockPlacement.From(p).Bounds(width, height, w, h);
        return new(left + bounds.X, top + bounds.Y, bounds.Width, bounds.Height);
    }
    public void Position(bool animate = true)
    {
        handleLabel.Update(Host.Label, Host.Session.State.Preferences.Edge);
        var expanded = BoundsFor(DrawerMode.Focused);
        content.Width = Math.Max(1, expanded.Width - 28);
        content.Height = Math.Max(1, expanded.Height - 28);
        content.HorizontalAlignment = Host.Session.State.Preferences.Edge == DockEdge.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        // Reserve a small spring envelope once, rather than moving/resizing the HWND per frame.
        // All smaller docked rectangles are contained within this envelope, including at corners.
        var display = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        positionedScreen = display; positionedScale = Scale;
        var envelope = DockPlacement.From(Host.Session.State.Preferences).Bounds(display.Width / Scale, display.Height / Scale,
            Math.Max(expanded.Width * 1.08, DockPlacement.HandleLength), Math.Max(expanded.Height * 1.08, DockPlacement.HandleLength));
        var nextHost = AlignToPixels(new Rect(display.Left / Scale + envelope.X, display.Top / Scale + envelope.Y, envelope.Width, envelope.Height));
        bool hostChanged = nextHost != hostBounds;
        hostBounds = nextHost;
        if (hostChanged) ApplyBounds(hostBounds);
        // Layout stays fixed too: changing Width/Height during a frame can leave WPF's old
        // arrange slot briefly visible. Only clip geometry and opacity animate below.
        shell.Width = hint.Width = hostBounds.Width;
        shell.Height = hint.Height = hostBounds.Height;
        var children = (System.Windows.Controls.Canvas)shell.Child;
        children.Width = hostBounds.Width; children.Height = hostBounds.Height;
        System.Windows.Controls.Canvas.SetLeft(content, expanded.Left - hostBounds.Left);
        System.Windows.Controls.Canvas.SetTop(content, expanded.Top - hostBounds.Top);
        var handle = BoundsFor(DrawerMode.Handle);
        handleFrame.Width = handle.Width; handleFrame.Height = handle.Height;
        System.Windows.Controls.Canvas.SetLeft(handleFrame, handle.Left - hostBounds.Left);
        System.Windows.Controls.Canvas.SetTop(handleFrame, handle.Top - hostBounds.Top);
        if (!animate)
        {
            StopAnimation();
            SettleVisuals();
            return;
        }
        BeginMotion();
    }
    private void ApplyBounds(Rect r) { Left = r.Left; Top = r.Top; Width = r.Width; Height = r.Height; }
    private Rect AlignToPixels(Rect bounds)
    {
        double Snap(double value) => Math.Round(value * Scale) / Scale;
        return new Rect(new Point(Snap(bounds.Left), Snap(bounds.Top)), new Point(Snap(bounds.Right), Snap(bounds.Bottom)));
    }
    private VisualPose PoseFor(DrawerMode mode)
    {
        var bounds = BoundsFor(mode);
        bool expanded = mode is DrawerMode.Preview or DrawerMode.Focused;
        return new(bounds.Width, bounds.Height, expanded ? 22 : 11,
            expanded ? 1 : 0, mode == DrawerMode.Handle ? 1 : 0, mode == DrawerMode.Hidden ? 1 : 0);
    }
    private void BeginMotion()
    {
        var target = PoseFor(Mode);
        motionWidth.Target = target.Width; motionHeight.Target = target.Height; motionRadius.Target = target.Radius;
        motionContent.Target = target.ContentOpacity; motionLabel.Target = target.LabelOpacity; motionHint.Target = target.HintOpacity;
        content.Visibility = Visibility.Visible;
        if (animating) return; // Retarget without resetting position or velocity.
        animating = true; lastFrame = null;
        CompositionTarget.Rendering += RenderFrame;
    }
    private void StopAnimation()
    {
        CompositionTarget.Rendering -= RenderFrame;
        animating = false; lastFrame = null;
    }
    private void RenderFrame(object? sender, EventArgs e)
    {
        var time = ((RenderingEventArgs)e).RenderingTime;
        if (lastFrame is { } previous) AdvanceAnimation((time - previous).TotalSeconds);
        if (animating) lastFrame = time;
    }
    private void AdvanceAnimation(double seconds)
    {
        bool closing = Mode == DrawerMode.Hidden;
        // Large panels get less overshoot, while handles retain a small elastic response.
        double extent = Math.Max(motionWidth.Target, motionHeight.Target);
        double damping = closing ? 1 : extent > 900 ? .94 : .86;
        motionWidth.Step(seconds, closing ? 30 : 24, damping);
        motionHeight.Step(seconds, closing ? 30 : 24, damping);
        motionRadius.Step(seconds, 28, 1);
        motionContent.Step(seconds, 28, 1);
        motionLabel.Step(seconds, 28, 1);
        motionHint.Step(seconds, 28, 1);
        pose = new(motionWidth.Value, motionHeight.Value, motionRadius.Value, motionContent.Value, motionLabel.Value, motionHint.Value);
        DrawPose();
        if (motionWidth.IsSettled(.04) && motionHeight.IsSettled(.04) && motionRadius.IsSettled(.01) &&
            motionContent.IsSettled(.001) && motionLabel.IsSettled(.001) && motionHint.IsSettled(.001))
        {
            StopAnimation(); SettleVisuals();
        }
    }
    private void DrawPose()
    {
        var display = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        var placement = DockPlacement.From(Host.Session.State.Preferences).Bounds(display.Width / Scale, display.Height / Scale,
            Math.Clamp(pose.Width, 1 / Scale, hostBounds.Width), Math.Clamp(pose.Height, 1 / Scale, hostBounds.Height));
        double width = placement.Width, height = placement.Height;
        var edge = Host.Session.State.Preferences.Edge;
        double x = display.Left / Scale + placement.X - hostBounds.Left;
        double y = display.Top / Scale + placement.Y - hostBounds.Top;
        visibleBounds = AlignToPixels(new Rect(x, y, width, height));
        x = visibleBounds.X; y = visibleBounds.Y; width = visibleBounds.Width; height = visibleBounds.Height;
        // Only the two outward corners are rounded: their radius may use the full
        // thickness, so a retracted handle keeps its curve instead of becoming a flat strip.
        double maximumRadius = edge == DockEdge.Top ? Math.Min(height, width / 2) : Math.Min(width, height / 2);
        double radius = Math.Min(pose.Radius, maximumRadius);
        var corners = edge switch
        {
            DockEdge.Top => new CornerRadius(0, 0, radius, radius),
            DockEdge.Left => new CornerRadius(0, radius, radius, 0),
            _ => new CornerRadius(radius, 0, 0, radius)
        };
        shell.Opacity = 1;
        content.Opacity = Math.Clamp(pose.ContentOpacity, 0, 1);
        handleLabel.Opacity = Math.Clamp(pose.LabelOpacity, 0, 1);
        hint.Opacity = Math.Clamp(pose.HintOpacity, 0, 1);
        var clip = Outline(width, height, corners).Clone();
        clip.Transform = new TranslateTransform(x, y); clip.Freeze();
        shell.Clip = clip;
    }
    private static Geometry Outline(double width, double height, CornerRadius radius)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new(radius.TopLeft, 0), true, true);
            path.LineTo(new(width - radius.TopRight, 0), true, false);
            path.QuadraticBezierTo(new(width, 0), new(width, radius.TopRight), true, false);
            path.LineTo(new(width, height - radius.BottomRight), true, false);
            path.QuadraticBezierTo(new(width, height), new(width - radius.BottomRight, height), true, false);
            path.LineTo(new(radius.BottomLeft, height), true, false);
            path.QuadraticBezierTo(new(0, height), new(0, height - radius.BottomLeft), true, false);
            path.LineTo(new(0, radius.TopLeft), true, false);
            path.QuadraticBezierTo(new(0, 0), new(radius.TopLeft, 0), true, false);
        }
        geometry.Freeze();
        return geometry;
    }
    private void SettleVisuals()
    {
        pose = PoseFor(Mode);
        motionWidth.Snap(pose.Width); motionHeight.Snap(pose.Height); motionRadius.Snap(pose.Radius);
        motionContent.Snap(pose.ContentOpacity); motionLabel.Snap(pose.LabelOpacity); motionHint.Snap(pose.HintOpacity);
        content.Visibility = Mode is DrawerMode.Preview or DrawerMode.Focused ? Visibility.Visible : Visibility.Collapsed;
        DrawPose();
    }
    private void Tick(object? sender, EventArgs e)
    {
        if (positionedScreen != System.Windows.Forms.Screen.PrimaryScreen!.Bounds || positionedScale != Scale) Position(false);
        if (IsTransferring || resizing || Canvas.HasActiveInteraction)
        {
            outside = false;
            return;
        }
        Native.GetCursorPos(out var pt);
        if (Mode == DrawerMode.Hidden)
        {
            // Only the resting hint reopens a collapsing tray; the shrinking panel is not a trigger.
            if (BoundsFor(DrawerMode.Hidden).Contains(new Point(pt.X / Scale, pt.Y / Scale))) SetMode(DrawerMode.Handle);
            outside = false;
            return;
        }
        var pointer = PointFromScreen(new Point(pt.X, pt.Y));
        bool inside = visibleBounds.Contains(pointer);
        if (inside) { outside = false; focusedPointerEntered = true; return; }
        if (Mode == DrawerMode.Focused && !focusedPointerEntered) return;
        if (!outside) { outside = true; outsideSince = DateTime.UtcNow; }
        if ((DateTime.UtcNow - outsideSince).TotalMilliseconds > (Mode == DrawerMode.Handle ? 240 : 320))
        { dragOver = false; Canvas.DropPoint = null; Collapse(); }
    }
    private void SetMode(DrawerMode mode)
    {
        if (Mode == mode) return;
        if (mode is DrawerMode.Focused or DrawerMode.Preview) app.BeforeOpen(this);
        bool wasExpanded = Mode is DrawerMode.Preview or DrawerMode.Focused;
        Mode = mode;
        outside = false;
        focusedPointerEntered = false;
        Native.SetNonActivating(this, mode != DrawerMode.Focused);
        // Preview and focused share the same geometry; activation must not restart the spring.
        if (!(wasExpanded && mode is DrawerMode.Preview or DrawerMode.Focused)) Position();
        Canvas.IsEnabled = mode is DrawerMode.Focused or DrawerMode.Preview;
    }
    public void OpenFocused()
    {
        if (app.IsPositionEditing) { app.OpenSettings(); return; }
        SetMode(DrawerMode.Focused);
        Canvas.RefreshFiles();
        Activate();
        Canvas.Focus();
    }
    public void SuspendForPositionEdit()
    {
        Collapse();
        Position(false);
        dragOver = false;
        Canvas.DropPoint = null;
        resizing = false;
        ReleaseMouseCapture();
        timer.Stop();
        Hide();
    }
    public void ResumeAfterPositionEdit()
    {
        Mode = DrawerMode.Hidden;
        Position(false);
        Show();
        timer.Start();
    }
    public void Collapse()
    {
        if (IsTransferring) return;
        Canvas.CommitEdit();
        Canvas.CancelGesture();
        SetMode(DrawerMode.Hidden);
    }
    public void Notify(string text)
    {
        notice.Text = text; notice.Visibility = Visibility.Visible; noticeTimer.Stop(); noticeTimer.Start();
    }
    public void RefreshLabel() { Title = "drawer · " + Host.Label; handleLabel.Update(Host.Label, Host.Session.State.Preferences.Edge); ToolTip = Host.Label; }
    public void ApplyAppearance()
    {
        var palette = DrawerPalette.For(Host.Session.State.Preferences.Theme);
        hint.Background = palette.Shell;
        shell.Background = palette.Shell; panel.Background = palette.Surface; panel.BorderBrush = palette.Border;
        handleLabel.Foreground = palette.Ink; notice.Foreground = palette.Ink; notice.Background = palette.Editor;
        Canvas.ApplyAppearance();
    }
    public void CloseDrawer() { retired = true; Canvas.Stop(); Close(); }
    public void RetractForExport() => SetMode(DrawerMode.Hidden);
    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        if (Mode != DrawerMode.Focused) { OpenFocused(); e.Handled = true; return; }
        Point inWindow = e.GetPosition(this);
        var point = new Point(inWindow.X - visibleBounds.X, inWindow.Y - visibleBounds.Y);
        if (point.X < 8 || point.X > visibleBounds.Width - 8 || point.Y > visibleBounds.Height - 8)
        {
            resizing = true;
            resizeStart = PointToScreen(inWindow);
            resizeSize = visibleBounds.Size;
            CaptureMouse();
            e.Handled = true;
        }
    }
    private void OnResize(object sender, MouseEventArgs e)
    {
        if (!resizing) return;
        var point = PointToScreen(e.GetPosition(this));
        var p = Host.Session.State.Preferences;
        double dx = (point.X - resizeStart.X) / Scale, dy = (point.Y - resizeStart.Y) / Scale;
        var screen = ScreenSize;
        double width = resizeSize.Width + dx * (p.Edge == DockEdge.Right ? -1 : p.Edge == DockEdge.Top ? 2 : 1);
        double height = resizeSize.Height + dy * (p.Edge == DockEdge.Top ? 1 : 2);
        DrawerSizing.SetPercentages(p, Math.Clamp(Math.Round(width / screen.Width * 100), 10, 90), Math.Clamp(Math.Round(height / screen.Height * 100), 10, 90));
        Position(false);
        e.Handled = true;
    }
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (app.Transfer.IsFromDrawer(e.Data, Host.Id)) { e.Effects = DragDropEffects.None; Canvas.DropPoint = null; return; }
        dragOver = true;
        outside = false;
        if (Mode != DrawerMode.Focused) SetMode(DrawerMode.Preview);
        bool copyAllowed = (e.AllowedEffects & DragDropEffects.Copy) != 0;
        e.Effects = copyAllowed && app.Transfer.CanRead(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        Canvas.DropPoint = e.Effects != DragDropEffects.None ? Canvas.ToCell(e.GetPosition(Canvas)) : null;
        Canvas.InvalidateVisual();
    }
    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        dragOver = false;
        Canvas.DropPoint = null;
        if (app.Transfer.IsFromDrawer(e.Data, Host.Id) || (e.AllowedEffects & DragDropEffects.Copy) == 0) { e.Effects = DragDropEffects.None; return; }
        var point = e.GetPosition(Canvas);
        if (!new Rect(0, 0, Canvas.ActualWidth, Canvas.ActualHeight).Contains(point)) { e.Effects = DragDropEffects.None; return; }
        try
        {
            var items = app.Transfer.Read(e.Data);
            Canvas.MeasureItems(items);
            Host.Session.Insert(items, Canvas.ToCell(point));
            e.Effects = items.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
            if (items.Count == 0) Notify("无法识别拖入内容");
        }
        catch { e.Effects = DragDropEffects.None; Notify("导入失败，抽屉内容保持不变"); }
        Canvas.InvalidateVisual();
    }
}
