using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Threading;

namespace Drawer.Windows;

public sealed class CanvasView : Panel
{
    private readonly App app;
    private readonly EdgeWindow window;
    private DrawerSession Session => app.Session;
    private DrawerState State => Session.State;
    private static readonly Typeface Font = new("Segoe UI");
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(225, 229, 237));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(120, 172, 255));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(148, 157, 174));
    private readonly Dictionary<Guid, ImageSource?> images = [];
    private readonly HashSet<Guid> loadingImages = [];
    private readonly Dictionary<Guid, bool> missingFiles = [];
    private TextBox? editor;
    private Guid? editId;
    private string? editBefore;
    private bool committing;
    private string? gesture;
    private Point pressPoint;
    private Cell pressCell;
    private Cell moveDelta;
    private Point panOrigin;
    private Rect marquee;
    private Rect deleteBounds;
    private HashSet<Guid> baseSelection = [];
    private bool moved;
    private bool contextMenuOpen;
    private bool modalOpen;
    public bool HasActiveInteraction => gesture is not null || IsMouseCaptureWithin || contextMenuOpen || modalOpen;
    private readonly DispatcherTimer viewportSave;
    public Cell? DropPoint { get; set; }
    public CanvasView(App app, EdgeWindow window)
    {
        this.app = app;
        this.window = window;
        Focusable = true;
        ClipToBounds = true;
        Background = Brushes.Transparent;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Session.Changed += () => { InvalidateVisual(); InvalidateArrange(); };
        viewportSave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        viewportSave.Tick += (_, _) => { viewportSave.Stop(); Session.Save(); };
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseWheel += OnWheel;
        ContextMenuOpening += (_, _) => contextMenuOpen = true;
        ContextMenuClosing += (_, _) => contextMenuOpen = false;
        KeyDown += OnKeyDown;
        SizeChanged += (_, _) => { UpdateEditor(); InvalidateVisual(); };
        LostMouseCapture += (_, _) => { if (!window.IsTransferring && gesture is not null) CancelGesture(); };
    }
    private Point World(Point screen) => new((screen.X - State.Viewport.OffsetX) / State.Viewport.Zoom, (screen.Y - State.Viewport.OffsetY) / State.Viewport.Zoom);
    public Cell ToCell(Point screen)
    {
        var world = World(screen);
        return new((int)Math.Clamp(Math.Floor(world.X / 28), -1_000_000, 1_000_000), (int)Math.Clamp(Math.Floor(world.Y / 28), -1_000_000, 1_000_000));
    }
    private Point Screen(Point world) => new(world.X * State.Viewport.Zoom + State.Viewport.OffsetX, world.Y * State.Viewport.Zoom + State.Viewport.OffsetY);
    private static Rect WorldRect(GridFrame frame) => new(frame.Origin.X * 28, frame.Origin.Y * 28, frame.Size.Width * 28, frame.Size.Height * 28);
    private Rect ScreenRect(GridFrame frame)
    {
        var r = WorldRect(frame);
        return new(Screen(r.TopLeft), Screen(r.BottomRight));
    }
    private DrawerItem? Hit(Point point) => State.Items.LastOrDefault(i => ScreenRect(i.Frame).Contains(point));
    private FormattedText Text(string text, double size = 14, Brush? brush = null)
        => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Font, size, brush ?? Ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    private FormattedText ItemText(DrawerItem i)
    {
        var t = Text(i.Text.Length == 0 ? " " : i.Text);
        t.MaxTextWidth = Math.Max(1, i.Frame.Size.Width * 28 - 16);
        t.LineHeight = 20;
        return t;
    }
    public void MeasureItems(IEnumerable<DrawerItem> items)
    {
        foreach (var i in items.Where(i => i.Kind == ItemKind.Text))
        {
            var estimate = ItemSizing.Text(i.Text);
            i.Frame = new(i.Frame.Origin, estimate);
            i.Frame = new(i.Frame.Origin, new(estimate.Width, Math.Max(1, (int)Math.Ceiling((ItemText(i).Height + 10) / 28))));
        }
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize), 13, 13));
        var v = State.Viewport;
        double step = 28 * v.Zoom;
        var dot = new SolidColorBrush(Color.FromRgb(55, 59, 66));
        for (double x = ((v.OffsetX % step) + step) % step; x < ActualWidth; x += step)
            for (double y = ((v.OffsetY % step) + step) % step; y < ActualHeight; y += step)
                dc.DrawEllipse(dot, null, new(x, y), Math.Clamp(v.Zoom * .75, .6, 1), Math.Clamp(v.Zoom * .75, .6, 1));
        dc.PushTransform(new TranslateTransform(v.OffsetX, v.OffsetY));
        dc.PushTransform(new ScaleTransform(v.Zoom, v.Zoom));
        foreach (var item in State.Items)
        {
            var frame = gesture == "move" && Session.Selection.Contains(item.Id) ? item.Frame.Translate(moveDelta) : item.Frame;
            if (!ScreenRect(frame).IntersectsWith(new Rect(RenderSize))) continue;
            DrawItem(dc, item, frame);
        }
        dc.Pop(); dc.Pop();
        deleteBounds = Rect.Empty;
        var selected = State.Items.Where(i => Session.Selection.Contains(i.Id)).ToList();
        if (selected.Count > 0 && editId is null)
        {
            var bounds = Rect.Empty;
            foreach (var i in selected) bounds.Union(ScreenRect(gesture == "move" ? i.Frame.Translate(moveDelta) : i.Frame));
            if (selected.Count > 1)
            {
                bounds.Inflate(4, 4);
                dc.DrawRoundedRectangle(null, new Pen(Accent, 1) { DashStyle = DashStyles.Dash }, bounds, 5, 5);
            }
            Point center = new(bounds.Right, bounds.Top);
            deleteBounds = new(center.X - 11, center.Y - 11, 22, 22);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(214, 75, 81)), null, center, 10, 10);
            dc.DrawLine(new Pen(Brushes.White, 1.5), new(center.X - 3, center.Y - 3), new(center.X + 3, center.Y + 3));
            dc.DrawLine(new Pen(Brushes.White, 1.5), new(center.X + 3, center.Y - 3), new(center.X - 3, center.Y + 3));
        }
        if (gesture == "marquee" && moved)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(30, 120, 172, 255)), new Pen(Accent, 1), marquee);
        if ((DropPoint ?? State.InsertionPoint) is Cell point)
        {
            var p = Screen(new Point(point.X * 28, point.Y * 28));
            var pen = new Pen(DropPoint is null ? Accent : Brushes.LightGreen, 1.3);
            dc.DrawLine(pen, new(p.X - 5, p.Y), new(p.X + 5, p.Y));
            dc.DrawLine(pen, new(p.X, p.Y - 5), new(p.X, p.Y + 5));
        }
        dc.Pop();
    }
    private void DrawItem(DrawingContext dc, DrawerItem item, GridFrame frame)
    {
        var r = WorldRect(frame);
        r.Inflate(-2, -2);
        bool selected = Session.Selection.Contains(item.Id);
        var fill = new SolidColorBrush(Color.FromArgb(item.Kind == ItemKind.Text ? (byte)12 : (byte)28, 160, 177, 201));
        dc.DrawRoundedRectangle(fill, selected ? new Pen(Accent, 2 / State.Viewport.Zoom) : item.Kind == ItemKind.Text ? null : new Pen(new SolidColorBrush(Color.FromArgb(20, 200, 210, 230)), 1), r, 11, 11);
        if (selected) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(14, 120, 172, 255)), null, r, 11, 11);
        if (item.Id == editId) return;
        if (item.Kind == ItemKind.Text)
        {
            if (State.Viewport.Zoom >= .5) dc.DrawText(ItemText(item), new(r.X + 6, r.Y + 3));
            return;
        }
        bool isFile = item.Kind == ItemKind.FileReference;
        Rect picture = isFile ? new(r.X + 21, r.Y + 8, r.Width - 42, 38) : new(r.X + 6, r.Y + 6, r.Width - 12, r.Height - 12);
        EnsureImage(item);
        if (images.TryGetValue(item.Id, out var image) && image is not null)
        {
            double ratio = image.Width / Math.Max(1, image.Height);
            double w = Math.Min(picture.Width, picture.Height * ratio), h = Math.Min(picture.Height, picture.Width / ratio);
            dc.DrawImage(image, new Rect(picture.X + (picture.Width - w) / 2, picture.Y + (picture.Height - h) / 2, w, h));
        }
        else
        {
            bool missing = missingFiles.GetValueOrDefault(item.Id);
            var pen = new Pen(missing ? Brushes.IndianRed : Muted, 1.5);
            var icon = new Rect(picture.X + (picture.Width - 25) / 2, picture.Y + (picture.Height - 30) / 2, 25, 30);
            dc.DrawRoundedRectangle(null, pen, icon, 3, 3);
            dc.DrawLine(pen, new(icon.Left + 5, icon.Top + 11), new(icon.Right - 5, icon.Top + 11));
            dc.DrawLine(pen, new(icon.Left + 5, icon.Top + 17), new(icon.Right - 5, icon.Top + 17));
            if (missing) dc.DrawLine(pen, icon.TopLeft, icon.BottomRight);
        }
        if (isFile && State.Viewport.Zoom >= .5)
        {
            string name = TruncateMiddle(item.DisplayName, r.Width - 8);
            var text = Text(name, 10, missingFiles.GetValueOrDefault(item.Id) ? Brushes.IndianRed : Ink);
            dc.DrawText(text, new(r.X + (r.Width - text.Width) / 2, r.Bottom - 23));
        }
    }
    private string TruncateMiddle(string value, double width)
    {
        if (Text(value, 10).Width <= width) return value;
        int left = Math.Max(1, value.Length / 2), right = Math.Max(1, value.Length - left);
        while (left + right > 2)
        {
            string candidate = value[..left] + "…" + value[^right..];
            if (Text(candidate, 10).Width <= width) return candidate;
            if (left > right) left--; else right--;
        }
        return value[..1] + "…" + value[^1..];
    }
    private async void EnsureImage(DrawerItem item)
    {
        if (images.ContainsKey(item.Id) || !loadingImages.Add(item.Id)) return;
        var result = await Task.Run(() =>
        {
            if (item.Kind == ItemKind.EmbeddedImage) return (Image: (ImageSource?)app.Transfer.LoadImage(item), Missing: false);
            string? path = app.Files.Resolve(item);
            if (path is null) return (Image: (ImageSource?)null, Missing: true);
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }.Contains(extension))
                return (Image: (ImageSource?)app.Transfer.LoadImage(item), Missing: false);
            return (Image: ShellIcon.Load(path), Missing: false);
        });
        images[item.Id] = result.Image;
        missingFiles[item.Id] = result.Missing;
        loadingImages.Remove(item.Id);
        InvalidateVisual();
    }
    public void RefreshFiles() { images.Clear(); missingFiles.Clear(); InvalidateVisual(); }
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (editor is not null && IsInsideEditor(e.OriginalSource as DependencyObject)) return;
        CommitEdit();
        Focus();
        pressPoint = e.GetPosition(this);
        pressCell = ToCell(pressPoint);
        moved = false;
        if (e.ChangedButton == MouseButton.Right)
        {
            gesture = "pan"; panOrigin = new(State.Viewport.OffsetX, State.Viewport.OffsetY); CaptureMouse(); e.Handled = true; return;
        }
        if (e.ChangedButton != MouseButton.Left) return;
        if (!deleteBounds.IsEmpty && deleteBounds.Contains(pressPoint)) { Session.Remove(Session.Selection.ToArray()); e.Handled = true; return; }
        var hit = Hit(pressPoint);
        bool append = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        if (hit is not null)
        {
            if (e.ClickCount == 2)
            {
                Session.Selection.Clear(); Session.Selection.Add(hit.Id);
                if (hit.Kind == ItemKind.Text) BeginEdit(hit, pressPoint);
                else if (hit.Kind == ItemKind.FileReference) OpenFile(hit, false);
                e.Handled = true; return;
            }
            if (append)
            {
                if (!Session.Selection.Remove(hit.Id)) Session.Selection.Add(hit.Id);
            }
            else if (!Session.Selection.Contains(hit.Id)) { Session.Selection.Clear(); Session.Selection.Add(hit.Id); }
            if (Session.Selection.Contains(hit.Id)) { gesture = "move"; moveDelta = default; CaptureMouse(); }
        }
        else
        {
            baseSelection = append ? Session.Selection.ToHashSet() : [];
            if (!append) Session.Selection.Clear();
            gesture = "marquee";
            marquee = new(pressPoint, pressPoint);
            CaptureMouse();
        }
        Session.Refresh();
        e.Handled = true;
    }
    private bool IsInsideEditor(DependencyObject? source)
    {
        while (source is not null) { if (source == editor) return true; source = source is Visual ? VisualTreeHelper.GetParent(source) : null; }
        return false;
    }
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (gesture is null) return;
        var p = e.GetPosition(this);
        if ((p - pressPoint).Length >= 4) moved = true;
        switch (gesture)
        {
            case "pan":
                if (!moved) break;
                State.Viewport.OffsetX = panOrigin.X + p.X - pressPoint.X;
                State.Viewport.OffsetY = panOrigin.Y + p.Y - pressPoint.Y;
                Cursor = Cursors.ScrollAll;
                break;
            case "move":
                if (!moved) break;
                moveDelta = ToCell(p) - pressCell;
                Point inWindow = TranslatePoint(p, window);
                var border = new Rect(-8, -8, window.ActualWidth + 16, window.ActualHeight + 16);
                if (!border.Contains(inWindow)) { StartExport(); return; }
                break;
            case "marquee":
                p = new(Math.Clamp(p.X, 0, ActualWidth), Math.Clamp(p.Y, 0, ActualHeight));
                marquee = new(pressPoint, p);
                if (!moved) break;
                Session.Selection.Clear(); Session.Selection.UnionWith(baseSelection);
                foreach (var item in State.Items)
                {
                    var bounds = ScreenRect(item.Frame);
                    if (State.Preferences.SelectionMustContain ? marquee.Contains(bounds) : marquee.IntersectsWith(bounds)) Session.Selection.Add(item.Id);
                }
                break;
        }
        InvalidateVisual();
        e.Handled = true;
    }
    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (gesture is null) return;
        var current = gesture;
        gesture = null;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        if (current == "move" && moved) Session.Move(moveDelta);
        if (current == "pan") { if (moved) Session.Save(); else ShowMenu(e.GetPosition(this)); }
        if (current == "marquee" && !moved) { State.InsertionPoint = pressCell; Session.Save(); }
        moveDelta = default;
        InvalidateVisual();
        e.Handled = true;
    }
    public void CancelGesture()
    {
        if (gesture == "pan") { State.Viewport.OffsetX = panOrigin.X; State.Viewport.OffsetY = panOrigin.Y; }
        gesture = null; moveDelta = default; ReleaseMouseCapture(); Cursor = Cursors.Arrow; InvalidateVisual();
    }
    private void StartExport()
    {
        gesture = null; moveDelta = default;
        ReleaseMouseCapture(); InvalidateVisual();
        window.IsTransferring = true;
        try
        {
            var output = app.Transfer.Build(State.Items.Where(i => Session.Selection.Contains(i.Id)), true);
            if (output.Exported.Count == 0) { window.Notify("没有可输出的内容"); return; }
            var result = DragDrop.DoDragDrop(this, output.Data, DragDropEffects.Copy);
            if (result == DragDropEffects.None) window.Notify("未放入目标，内容仍在抽屉中");
        }
        catch { window.Notify("目标不支持当前内容，可尝试逐个拖出"); }
        finally
        {
            window.IsTransferring = false;
            if (!app.InspectionMode && !Native.OurAppIsForeground()) window.Collapse();
        }
    }
    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (window.Mode != DrawerMode.Focused) return;
        CommitEdit();
        var v = State.Viewport;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            var pointer = e.GetPosition(this);
            var before = World(pointer);
            v.Zoom = Math.Clamp(v.Zoom * Math.Pow(1.12, e.Delta / 120d), .35, 2.5);
            v.OffsetX = pointer.X - before.X * v.Zoom; v.OffsetY = pointer.Y - before.Y * v.Zoom;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) v.OffsetX += e.Delta / 3d;
        else v.OffsetY += e.Delta / 3d;
        viewportSave.Stop(); viewportSave.Start();
        InvalidateVisual();
        e.Handled = true;
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (editor is not null || window.Mode != DrawerMode.Focused) return;
        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (control)
        {
            switch (e.Key)
            {
                case Key.A: Session.Selection.UnionWith(State.Items.Select(i => i.Id)); Session.Refresh(); break;
                case Key.C: Copy(false); break;
                case Key.X: Copy(true); break;
                case Key.V: Paste(); break;
                case Key.Z: if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) Session.Redo(); else Session.Undo(); break;
                case Key.Y: Session.Redo(); break;
                default: return;
            }
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back) { Session.Remove(Session.Selection.ToArray()); e.Handled = true; }
    }
    private void Copy(bool cut)
    {
        try
        {
            var selected = State.Items.Where(i => Session.Selection.Contains(i.Id)).ToList();
            var ids = app.Transfer.Copy(selected);
            if (ids.Count == 0) window.Notify("没有可复制的内容");
            else if (cut) Session.Remove(ids);
            if (ids.Count > 0 && ids.Count < selected.Count) window.Notify("已复制可用内容，失效对象保留");
        }
        catch { window.Notify("剪贴板暂时不可用，请重试"); }
    }
    private void Paste()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null) return;
            var items = app.Transfer.Read(data);
            MeasureItems(items);
            if (items.Count == 0) { window.Notify("无法识别剪贴板内容"); return; }
            Session.Insert(items, State.InsertionPoint ?? ToCell(new(ActualWidth / 2, ActualHeight / 2)));
        }
        catch { window.Notify("粘贴失败，请检查剪贴板或图片数据"); }
    }
    private void BeginEdit(DrawerItem item, Point point)
    {
        editBefore = State.Serialize();
        editId = item.Id;
        editor = new TextBox
        {
            Text = item.Text, FontFamily = new FontFamily("Segoe UI"), FontSize = 14,
            Foreground = Ink, Background = new SolidColorBrush(Color.FromRgb(29, 32, 39)),
            BorderThickness = new Thickness(0), Padding = new Thickness(6, 3, 6, 3),
            AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            SelectionBrush = Accent, IsUndoEnabled = true, SpellCheck = { IsEnabled = false }
        };
        editor.Template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="TextBox">
              <Border Background="{TemplateBinding Background}">
                <ScrollViewer x:Name="PART_ContentHost" Focusable="False" Margin="0" />
              </Border>
            </ControlTemplate>
            """);
        TextBlock.SetLineHeight(editor, 20);
        TextBlock.SetLineStackingStrategy(editor, LineStackingStrategy.BlockLineHeight);
        editor.TextChanged += (_, _) =>
        {
            if (editor is null) return;
            item.Text = editor.Text; MeasureItems([item]); UpdateEditor(); InvalidateVisual();
        };
        editor.LostKeyboardFocus += (_, _) => { if (!window.IsTransferring && !contextMenuOpen) CommitEdit(); };
        // Override editor's default move semantics: dragging a substring always copies it.
        Point textPress = default;
        string? dragText = null;
        editor.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (editor is null) return;
            textPress = e.GetPosition(editor);
            int index = editor.GetCharacterIndexFromPoint(textPress, true);
            dragText = index >= editor.SelectionStart && index < editor.SelectionStart + editor.SelectionLength ? editor.SelectedText : null;
            if (!string.IsNullOrEmpty(dragText)) e.Handled = true;
        };
        editor.PreviewMouseMove += (_, e) =>
        {
            if (editor is null || e.LeftButton != MouseButtonState.Pressed || string.IsNullOrEmpty(dragText) || (e.GetPosition(editor) - textPress).Length < 5) return;
            var data = new DataObject(DataFormats.UnicodeText, dragText);
            data.SetData(TransferService.SessionFormat, app.Transfer.Token);
            dragText = null;
            window.IsTransferring = true;
            try { DragDrop.DoDragDrop(editor, data, DragDropEffects.Copy); }
            finally
            {
                window.IsTransferring = false;
                if (!app.InspectionMode && !Native.OurAppIsForeground()) window.Collapse();
            }
            e.Handled = true;
        };
        editor.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (editor is not null && dragText is not null)
            {
                int index = editor.GetCharacterIndexFromPoint(e.GetPosition(editor), true);
                editor.Select(Math.Max(0, index), 0); dragText = null; e.Handled = true;
            }
        };
        Children.Add(editor);
        UpdateEditor(); UpdateLayout();
        editor.Focus();
        int caret = editor.GetCharacterIndexFromPoint(TranslatePoint(point, editor), true);
        editor.CaretIndex = Math.Max(0, caret);
        InvalidateVisual();
    }
    private void UpdateEditor()
    {
        if (editor is null || editId is null) return;
        var item = State.Items.FirstOrDefault(i => i.Id == editId);
        if (item is null) return;
        editor.Width = item.Frame.Size.Width * 28 - 4;
        editor.Height = item.Frame.Size.Height * 28 - 4;
        editor.RenderTransform = new ScaleTransform(State.Viewport.Zoom, State.Viewport.Zoom);
        InvalidateMeasure(); InvalidateArrange();
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        editor?.Measure(new Size(editor.Width, editor.Height));
        return new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 500 : availableSize.Height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (editor is not null && State.Items.FirstOrDefault(i => i.Id == editId) is DrawerItem item)
        {
            var p = Screen(new(item.Frame.Origin.X * 28 + 2, item.Frame.Origin.Y * 28 + 2));
            editor.Arrange(new Rect(p, new Size(editor.Width, editor.Height)));
        }
        return finalSize;
    }
    public void CommitEdit()
    {
        if (editor is null || committing) return;
        committing = true;
        var box = editor; editor = null;
        var id = editId; editId = null;
        Children.Remove(box);
        if (State.Items.FirstOrDefault(i => i.Id == id) is DrawerItem item)
        {
            item.Text = box.Text; MeasureItems([item]); LayoutEngine.Reflow(State.Items, item.Id);
            Session.Commit(editBefore!);
        }
        editBefore = null; committing = false; InvalidateVisual();
    }
    public void FitAll()
    {
        CommitEdit();
        if (State.Items.Count == 0) return;
        var bounds = Rect.Empty;
        foreach (var i in State.Items) bounds.Union(WorldRect(i.Frame));
        var v = State.Viewport;
        v.Zoom = Math.Clamp(Math.Min(Math.Max(1, ActualWidth - 64) / bounds.Width, Math.Max(1, ActualHeight - 64) / bounds.Height), .35, 1.5);
        v.OffsetX = ActualWidth / 2 - (bounds.X + bounds.Width / 2) * v.Zoom;
        v.OffsetY = ActualHeight / 2 - (bounds.Y + bounds.Height / 2) * v.Zoom;
        Session.Save(); InvalidateVisual();
    }
    private void ShowMenu(Point point)
    {
        var hit = Hit(point);
        if (hit is not null && !Session.Selection.Contains(hit.Id)) { Session.Selection.Clear(); Session.Selection.Add(hit.Id); Session.Refresh(); }
        var menu = new ContextMenu();
        menu.Opened += (_, _) => contextMenuOpen = true;
        menu.Closed += (_, _) => contextMenuOpen = false;
        void Add(string name, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = name, IsEnabled = enabled };
            item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        if (hit is not null || Session.Selection.Count > 0)
        {
            Add("复制", () => Copy(false)); Add("剪切", () => Copy(true));
            Add("从抽屉中移除", () => Session.Remove(Session.Selection.ToArray()));
            if (hit?.Kind == ItemKind.FileReference)
            {
                bool valid = app.Files.Resolve(hit) is not null;
                Add("打开", () => OpenFile(hit, false), valid);
                Add("在文件管理器中显示", () => OpenFile(hit, true), valid);
            }
            menu.Items.Add(new Separator());
        }
        Add("粘贴", Paste);
        Add("撤销", Session.Undo, Session.CanUndo); Add("重做", Session.Redo, Session.CanRedo);
        menu.Items.Add(new Separator());
        Add("显示全部", FitAll, State.Items.Count > 0);
        Add("回到落点", () =>
        {
            if (State.InsertionPoint is not Cell cell) return;
            State.Viewport.OffsetX = ActualWidth / 2 - cell.X * 28 * State.Viewport.Zoom;
            State.Viewport.OffsetY = ActualHeight / 2 - cell.Y * 28 * State.Viewport.Zoom;
            Session.Save(); InvalidateVisual();
        }, State.InsertionPoint is not null);
        var rule = new MenuItem { Header = "框选方式" };
        foreach (var (name, contain) in new[] { ("相交即选中", false), ("完全包含才选中", true) })
        {
            var option = new MenuItem { Header = name, IsCheckable = true, IsChecked = State.Preferences.SelectionMustContain == contain };
            option.Click += (_, _) => { State.Preferences.SelectionMustContain = contain; Session.Save(); };
            rule.Items.Add(option);
        }
        menu.Items.Add(rule);
        Add("清空抽屉…", () =>
        {
            modalOpen = true;
            try
            {
                if (MessageBox.Show(window, "文字和截图会从抽屉移除。\n文件只移除引用，不修改源文件。\n\n此操作可以撤销。", "清空抽屉", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
                    Session.Remove(State.Items.Select(i => i.Id).ToArray());
            }
            finally { modalOpen = false; }
        }, State.Items.Count > 0);
        menu.Items.Add(new Separator()); Add("退出 drawer", app.ExitApp);
        menu.PlacementTarget = this; menu.IsOpen = true;
    }
    private void OpenFile(DrawerItem item, bool reveal)
    {
        try
        {
            string? path = app.Files.Resolve(item);
            if (path is null) { window.Notify("源文件当前不可用"); RefreshFiles(); return; }
            if (reveal) FileReferenceService.Reveal(path); else FileReferenceService.Open(path);
        }
        catch { window.Notify("无法打开此文件"); }
    }
}
