using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;

namespace Drawer.Windows;

public sealed class EdgeWindow : Window
{
    private readonly App app;
    private readonly Border shell;
    private readonly Grid content;
    private readonly TextBlock notice;
    private readonly DispatcherTimer timer;
    private readonly DispatcherTimer noticeTimer;
    private DateTime outsideSince = DateTime.UtcNow;
    private bool outside;
    private bool focusedPointerEntered;
    private bool dragOver;
    private enum MotionPhase { None, HandleIn, Expand, CollapseToHandle, HandleOut }
    private readonly record struct VisualPose(double Width, double Height, double Radius, double ShellOpacity, double ContentOpacity);
    private VisualPose pose, fromPose, toPose;
    private MotionPhase motion;
    private readonly Stopwatch animationClock = new();
    private double animationDuration;
    private bool animating;
    private bool resizing;
    private Point resizeStart;
    private Size resizeSize;
    public DrawerMode Mode { get; private set; } = DrawerMode.Hidden;
    public bool IsTransferring { get; set; }
    public CanvasView Canvas { get; }
    private double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;
    public EdgeWindow(App app)
    {
        this.app = app;
        Title = "drawer";
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
        var panel = new Border
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
        shell = new Border { Background = Brushes.Black, Child = content, Opacity = 0, ClipToBounds = true };
        // The invisible sensor belongs to the HWND; the visible shell can genuinely start at 0×0.
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Content = shell;
        noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        noticeTimer.Tick += (_, _) => { notice.Visibility = Visibility.Collapsed; noticeTimer.Stop(); };
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += Tick;
        SourceInitialized += (_, _) => { Native.SetNonActivating(this, true); Position(false); timer.Start(); };
        Closed += (_, _) => { timer.Stop(); noticeTimer.Stop(); };
        Closing += (_, e) => { if (!app.IsExiting) { e.Cancel = true; Collapse(); } };
        MouseEnter += (_, _) => { outside = false; if (Mode == DrawerMode.Hidden && !animating) SetMode(DrawerMode.Handle); };
        PreviewMouseLeftButtonDown += OnPress;
        PreviewMouseMove += OnResize;
        PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (!resizing) return;
            resizing = false; ReleaseMouseCapture(); app.Session.Save();
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
        var p = app.Session.State.Preferences;
        bool expanded = mode is DrawerMode.Preview or DrawerMode.Focused;
        double w = expanded ? Math.Clamp(p.Width, 360, Math.Min(900, Math.Max(360, width))) : p.Edge == DockEdge.Top ? 132 : mode == DrawerMode.Hidden ? 2 / scale : 15;
        double h = expanded ? Math.Clamp(p.Height, 420, Math.Min(1000, Math.Max(420, height))) : p.Edge == DockEdge.Top ? mode == DrawerMode.Hidden ? 2 / scale : 15 : 132;
        return p.Edge switch
        {
            DockEdge.Left => new(left, top + (height - h) / 2, w, h),
            DockEdge.Right => new(left + width - w, top + (height - h) / 2, w, h),
            _ => new(left + (width - w) / 2, top, w, h)
        };
    }
    public void Position(bool animate = true)
    {
        // Keep the canvas layout stable while the outer shell changes size. Resizing the HWND
        // must not repeatedly reflow content or move the viewport center during an animation.
        var expanded = BoundsFor(DrawerMode.Focused);
        content.Width = Math.Max(1, expanded.Width - 28);
        content.Height = Math.Max(1, expanded.Height - 28);
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        if (!animate)
        {
            animating = false;
            motion = MotionPhase.None;
            SettleVisuals();
            return;
        }
        if (Mode == DrawerMode.Hidden)
        {
            // Do not hide the content here: it remains visible during the shrink/fade.
            if (pose.ContentOpacity > 0 || pose.Width > BoundsFor(DrawerMode.Handle).Width + 1 || pose.Height > BoundsFor(DrawerMode.Handle).Height + 1)
                BeginMotion(MotionPhase.CollapseToHandle, PoseFor(DrawerMode.Handle), 220);
            else BeginMotion(MotionPhase.HandleOut, new(0, 0, 0, 0, 0), 100);
        }
        else if (Mode == DrawerMode.Handle) BeginMotion(MotionPhase.HandleIn, PoseFor(Mode), 360);
        else BeginMotion(MotionPhase.Expand, PoseFor(Mode), 380);
    }
    private void ApplyBounds(Rect r) { Left = r.Left; Top = r.Top; Width = r.Width; Height = r.Height; }
    private VisualPose PoseFor(DrawerMode mode)
    {
        if (mode == DrawerMode.Hidden) return new(0, 0, 0, 0, 0);
        var bounds = BoundsFor(mode);
        bool expanded = mode is DrawerMode.Preview or DrawerMode.Focused;
        return new(bounds.Width, bounds.Height, expanded ? 22 : 11, 1, expanded ? 1 : 0);
    }
    private void BeginMotion(MotionPhase phase, VisualPose target, double duration)
    {
        fromPose = pose;
        toPose = target;
        motion = phase;
        animationDuration = duration;
        animationClock.Restart();
        animating = true;
        // Keep the panel alive until its fade completes, including interrupted transitions.
        content.Visibility = fromPose.ContentOpacity > 0 || target.ContentOpacity > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private static double Spring(double t, double damping, double frequency)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        return 1 - Math.Exp(-damping * t) * (Math.Cos(frequency * t) + damping / frequency * Math.Sin(frequency * t));
    }
    private void AdvanceAnimation()
    {
        double t = Math.Clamp(animationClock.Elapsed.TotalMilliseconds / animationDuration, 0, 1);
        double eased = motion switch
        {
            MotionPhase.HandleIn => Spring(t, 6.5, 10),
            MotionPhase.Expand => Spring(t, 9.5, 10.5),
            _ => t * t * (3 - 2 * t)
        };
        double fade = t * t * (3 - 2 * t);
        static double Mix(double a, double b, double p) => a + (b - a) * p;
        pose = new(
            Math.Max(0, Mix(fromPose.Width, toPose.Width, eased)),
            Math.Max(0, Mix(fromPose.Height, toPose.Height, eased)),
            Math.Max(0, Mix(fromPose.Radius, toPose.Radius, fade)),
            Mix(fromPose.ShellOpacity, toPose.ShellOpacity, fade),
            Mix(fromPose.ContentOpacity, toPose.ContentOpacity, fade));
        DrawPose();
        if (t < 1) return;
        if (motion == MotionPhase.CollapseToHandle)
        {
            BeginMotion(MotionPhase.HandleOut, new(0, 0, 0, 0, 0), 100);
            return;
        }
        animating = false;
        motion = MotionPhase.None;
        SettleVisuals();
    }
    private void DrawPose()
    {
        var anchor = BoundsFor(DrawerMode.Handle);
        double width = Math.Max(1 / Scale, pose.Width), height = Math.Max(1 / Scale, pose.Height);
        var edge = app.Session.State.Preferences.Edge;
        var bounds = edge switch
        {
            DockEdge.Left => new Rect(anchor.Left, anchor.Top + anchor.Height / 2 - height / 2, width, height),
            DockEdge.Right => new Rect(anchor.Right - width, anchor.Top + anchor.Height / 2 - height / 2, width, height),
            _ => new Rect(anchor.Left + anchor.Width / 2 - width / 2, anchor.Top, width, height)
        };
        ApplyBounds(bounds);
        double radius = Math.Min(pose.Radius, Math.Min(width, height) / 2);
        shell.CornerRadius = edge switch
        {
            DockEdge.Top => new(0, 0, radius, radius),
            DockEdge.Left => new(0, radius, radius, 0),
            _ => new(radius, 0, 0, radius)
        };
        shell.Opacity = Math.Clamp(pose.ShellOpacity, 0, 1);
        content.Opacity = Math.Clamp(pose.ContentOpacity, 0, 1);
        // Clip the fixed-size canvas to the moving outline, including the interior round corners.
        shell.Clip = Outline(width, height, shell.CornerRadius);
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
        content.Visibility = Mode is DrawerMode.Preview or DrawerMode.Focused ? Visibility.Visible : Visibility.Collapsed;
        if (Mode == DrawerMode.Hidden)
        {
            shell.Opacity = 0;
            content.Opacity = 0;
            shell.Clip = null;
            ApplyBounds(BoundsFor(DrawerMode.Hidden));
        }
        else DrawPose();
    }
    private void Tick(object? sender, EventArgs e)
    {
        if (animating) AdvanceAnimation();
        if (Mode == DrawerMode.Hidden || IsTransferring || resizing || Canvas.HasActiveInteraction || animating)
        {
            // After a gesture/menu finishes, give the pointer a fresh leave delay.
            outside = false;
            return;
        }
        Native.GetCursorPos(out var pt);
        var pointer = PointFromScreen(new Point(pt.X, pt.Y));
        bool inside = new Rect(0, 0, ActualWidth, ActualHeight).Contains(pointer);
        if (inside) { outside = false; focusedPointerEntered = true; return; }
        // A launch/notification can open the drawer while the pointer is elsewhere.
        // Auto-hide begins after the user has actually entered and then left it.
        if (Mode == DrawerMode.Focused && !focusedPointerEntered) return;
        if (!outside) { outside = true; outsideSince = DateTime.UtcNow; }
        if ((DateTime.UtcNow - outsideSince).TotalMilliseconds > (Mode == DrawerMode.Handle ? 240 : 320))
        { dragOver = false; Canvas.DropPoint = null; Collapse(); }
    }
    private void SetMode(DrawerMode mode)
    {
        if (Mode == mode) return;
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
        SetMode(DrawerMode.Focused);
        Canvas.RefreshFiles();
        Activate();
        Canvas.Focus();
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
    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        if (Mode != DrawerMode.Focused) { OpenFocused(); e.Handled = true; return; }
        Point point = e.GetPosition(this);
        if (point.X < 8 || point.X > ActualWidth - 8 || point.Y > ActualHeight - 8)
        {
            resizing = true;
            resizeStart = PointToScreen(point);
            resizeSize = new(Width, Height);
            CaptureMouse();
            e.Handled = true;
        }
    }
    private void OnResize(object sender, MouseEventArgs e)
    {
        if (!resizing) return;
        var point = PointToScreen(e.GetPosition(this));
        var p = app.Session.State.Preferences;
        double dx = (point.X - resizeStart.X) / Scale, dy = (point.Y - resizeStart.Y) / Scale;
        p.Width = Math.Clamp(resizeSize.Width + dx * (p.Edge == DockEdge.Right ? -1 : p.Edge == DockEdge.Top ? 2 : 1), 360, 900);
        p.Height = Math.Clamp(resizeSize.Height + dy * (p.Edge == DockEdge.Top ? 1 : 2), 420, 1000);
        Position(false);
        e.Handled = true;
    }
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (app.Transfer.IsOwn(e.Data)) { e.Effects = DragDropEffects.None; Canvas.DropPoint = null; return; }
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
        if (app.Transfer.IsOwn(e.Data) || (e.AllowedEffects & DragDropEffects.Copy) == 0) { e.Effects = DragDropEffects.None; return; }
        var point = e.GetPosition(Canvas);
        if (!new Rect(0, 0, Canvas.ActualWidth, Canvas.ActualHeight).Contains(point)) { e.Effects = DragDropEffects.None; return; }
        try
        {
            var items = app.Transfer.Read(e.Data);
            Canvas.MeasureItems(items);
            app.Session.Insert(items, Canvas.ToCell(point));
            e.Effects = items.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
            if (items.Count == 0) Notify("无法识别拖入内容");
        }
        catch { e.Effects = DragDropEffects.None; Notify("导入失败，抽屉内容保持不变"); }
        Canvas.InvalidateVisual();
    }
}
