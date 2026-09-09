using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Drawer.Windows;

public sealed class DrawerHost
{
    public Guid Id { get; }
    public string Label { get; set; }
    public DrawerSession Session { get; }
    public EdgeWindow Window { get; }
    public DrawerHost(App app, DrawerRecord record)
    {
        Id = record.Id; Label = record.Label; Session = new(record.State);
        Window = new(app, this);
    }
    public override string ToString() => Label;
}

public partial class App : Application
{
    private Mutex? mutex;
    private EventWaitHandle? activateRequest;
    private System.Windows.Threading.DispatcherTimer? activateTimer;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private SettingsWindow? settings;
    private PositionEditorWindow? positionEditor;
    private GlobalShortcutService? shortcuts;
    private readonly Dictionary<Guid, string> shortcutErrors = [];
    public bool IsShortcutDialogOpen { get; set; }
    public List<DrawerHost> Drawers { get; } = [];
    public event Action? DrawersChanged;
    public string SettingsPage { get; set; } = "基础";
    public bool IsPositionEditing => positionEditor is not null;
    public bool IsTransferring => Drawers.Any(d => d.Window.IsTransferring);
    public PersistenceStore Store { get; private set; } = null!;
    public EdgeWindow Edge => Drawers[0].Window;
    public TransferService Transfer { get; private set; } = null!;
    public FileReferenceService Files { get; private set; } = null!;
    public bool InspectionMode { get; private set; }
    public bool IsExiting { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--verify-package") { Shutdown(PackageVerification.Run(e.Args[1])); return; }
        InspectionMode = e.Args.Contains("--inspect");
        string instanceName = InspectionMode ? "Local\\drawer.windows.inspect" : "Local\\drawer.windows";
        activateRequest = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName + ".activate");
        mutex = new Mutex(true, instanceName, out bool first);
        if (!first) { activateRequest.Set(); Shutdown(); return; }
        try
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "drawer");
            if (InspectionMode) root = Path.Combine(AppContext.BaseDirectory, "inspection-data");
            Store = new(root);
            var loaded = Store.LoadWorkspace();
            SettingsPage = loaded.State.SettingsPage;
            Files = new(root); Transfer = new(Store, Files);
            foreach (var record in loaded.State.Drawers) AddHost(record);
            MainWindow = Edge;
            CreateTray();
            if (!InspectionMode)
            {
                try { new StartupRegistration().RefreshEnabledPath(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
                { tray?.ShowBalloonTip(4000, "drawer · 开机自启", "自启路径更新失败，请在偏好设置中重新设置。", Forms.ToolTipIcon.Warning); }
            }
            activateTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
            activateTimer.Tick += (_, _) => { if (activateRequest.WaitOne(0)) Edge.OpenFocused(); };
            activateTimer.Start();
            // Commit the migrated workspace before resource collection; the previous single drawer remains in backup.
            Store.Save(CaptureWorkspace());
            shortcuts = new(id =>
            {
                if (IsExiting || IsPositionEditing || IsShortcutDialogOpen || IsTransferring || Drawers.Any(d => d.Window.Canvas.HasActiveInteraction)) return;
                Drawers.FirstOrDefault(d => d.Id == id)?.Window.OpenFocused();
            });
            foreach (var host in Drawers)
                if (!shortcuts.Bindings.TryApply(host.Id, host.Session.State.Preferences.OpenShortcut, () => { }, out string? error))
                    shortcutErrors[host.Id] = error!;
            if (shortcutErrors.Count > 0)
                tray?.ShowBalloonTip(5000, "drawer · 快捷键未启用", "部分抽屉的快捷键无法注册，请在偏好设置中更换。仍可通过屏幕边缘打开抽屉。", Forms.ToolTipIcon.Info);
            try { Store.CollectImages(CaptureWorkspace()); } catch { }
            CollectUnusedReferences();
            if (loaded.Notice is not null) { Edge.OpenFocused(); Edge.Notify(loaded.Notice); }
            if (e.Args.Contains("--show") || InspectionMode) Edge.OpenFocused();
            if (e.Args.Contains("--smoke-test"))
            {
                Edge.OpenFocused();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) => { timer.Stop(); ExitApp(); }; timer.Start();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex is FutureSchemaException ? ex.Message : "drawer 启动失败，请检查数据目录的访问权限。\n" + ex.Message, "drawer", MessageBoxButton.OK, MessageBoxImage.Error);
            IsExiting = true; Shutdown(1);
        }
    }
    private void AddHost(DrawerRecord record)
    {
        var host = new DrawerHost(this, record);
        Drawers.Add(host);
        host.Session.Committed += Save;
        host.Window.Show();
    }
    public DrawerWorkspace CaptureWorkspace() => new()
    {
        SettingsPage = SettingsPage,
        Drawers = Drawers.Select(d => new DrawerRecord { Id = d.Id, Label = d.Label, State = d.Session.State }).ToList()
    };
    public bool ChangeDrawers(Action<DrawerWorkspace> change, out string? error)
    {
        error = null;
        if (IsTransferring || IsPositionEditing) { error = "请先结束拖放或位置编辑。"; return false; }
        foreach (var host in Drawers) host.Window.Canvas.CommitEdit();
        try
        {
            var candidate = DrawerWorkspace.Deserialize(CaptureWorkspace().Serialize());
            change(candidate); candidate.Validate(); Store.Save(candidate);
            foreach (var removed in Drawers.Where(d => candidate.Drawers.All(r => r.Id != d.Id)).ToList())
            {
                shortcuts?.Bindings.Remove(removed.Id); shortcutErrors.Remove(removed.Id);
                removed.Session.Committed -= Save; Drawers.Remove(removed); removed.Window.CloseDrawer();
            }
            foreach (var record in candidate.Drawers)
            {
                var host = Drawers.FirstOrDefault(d => d.Id == record.Id);
                if (host is null) AddHost(record);
                else { host.Label = record.Label; host.Window.RefreshLabel(); }
            }
            MainWindow = Edge;
            DrawersChanged?.Invoke();
            CollectUnusedReferences();
            return true;
        }
        catch (InvalidDataException ex) { error = ex.Message; return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { error = "保存失败，抽屉列表保持不变，请重试。"; return false; }
    }
    public void SavePlacements(IReadOnlyDictionary<Guid, DockPlacement> placements)
    {
        var candidate = CaptureWorkspace().WithPlacements(placements);
        Store.Save(candidate);
        foreach (var host in Drawers)
        {
            var placement = placements[host.Id];
            host.Session.State.Preferences.Edge = placement.Edge;
            host.Session.State.Preferences.EdgePosition = placement.Position;
        }
    }
    public string ShortcutStatus(DrawerHost host)
    {
        var binding = host.Session.State.Preferences.OpenShortcut;
        if (binding is null) return "未设置";
        return shortcutErrors.TryGetValue(host.Id, out var error) ? $"{binding} · 未生效：{error}" : binding.ToString()!;
    }
    public void RecordShortcut(Action<ShortcutBinding>? recorder)
    {
        if (shortcuts is not null) shortcuts.Recorder = recorder;
    }
    public bool SetShortcut(Guid id, ShortcutBinding? binding, out string? error)
    {
        var host = Drawers.FirstOrDefault(d => d.Id == id);
        if (host is null || shortcuts is null) { error = "抽屉或快捷键服务不可用，请重试。"; return false; }
        var duplicate = Drawers.FirstOrDefault(d => d.Id != id && binding is not null && d.Session.State.Preferences.OpenShortcut == binding);
        if (duplicate is not null) { error = $"此快捷键已绑定到抽屉“{duplicate.Label}”。"; return false; }
        bool saved = shortcuts.Bindings.TryApply(id, binding, () =>
        {
            var candidate = DrawerWorkspace.Deserialize(CaptureWorkspace().Serialize());
            candidate.Drawers.Single(d => d.Id == id).State.Preferences.OpenShortcut = binding;
            Store.Save(candidate);
            host.Session.State.Preferences.OpenShortcut = binding;
        }, out error);
        if (saved) shortcutErrors.Remove(id);
        return saved;
    }
    public bool ConfigureDrawer(Guid id, Action<Preferences> change, out string? error, string? label = null)
    {
        error = null;
        if (IsTransferring || IsPositionEditing) { error = "请先结束拖放或位置编辑。"; return false; }
        var host = Drawers.FirstOrDefault(d => d.Id == id);
        if (host is null) { error = "抽屉不存在。"; return false; }
        host.Window.Canvas.CommitEdit();
        try
        {
            var candidate = DrawerWorkspace.Deserialize(CaptureWorkspace().Serialize());
            var preferences = candidate.Drawers.Single(d => d.Id == id).State.Preferences;
            if (label is not null) candidate.Rename(id, label);
            change(preferences); candidate.Validate();
            Store.Save(candidate);
            host.Session.State.Preferences = preferences;
            host.Label = candidate.Drawers.Single(d => d.Id == id).Label;
            host.Window.RefreshLabel(); host.Window.ApplyAppearance(); host.Window.Position(false);
            DrawersChanged?.Invoke();
            return true;
        }
        catch (InvalidDataException ex) { error = ex.Message; return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { error = "保存失败，原设置保持不变，请重试。"; return false; }
    }
    public bool SetCommandShortcut(Guid id, DrawerCommand command, ShortcutBinding? binding, out string? error) => ConfigureDrawer(id, p =>
    {
        if (binding is null) p.CommandShortcuts.Remove(command);
        else p.CommandShortcuts[command] = binding.Value;
    }, out error);
    public void BeforeOpen(EdgeWindow window)
    {
        foreach (var host in Drawers)
            if (host.Window != window && !host.Window.IsTransferring) host.Window.Collapse();
    }
    private void CreateTray()
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };
        void AddMenuItem(string title, Action action)
        {
            var item = new System.Windows.Controls.MenuItem { Header = title };
            item.Click += (_, _) =>
            {
                menu.IsOpen = false;
                // Let menu input and focus cleanup finish before activating another window.
                Dispatcher.BeginInvoke(action, System.Windows.Threading.DispatcherPriority.Background);
            };
            menu.Items.Add(item);
        }
        AddMenuItem("⚙  偏好设置…", OpenSettings);
        menu.Items.Add(new System.Windows.Controls.Separator());
        AddMenuItem("⏻  退出 drawer", ExitApp);
        trayIcon = AppIcon.CreateTrayIcon();
        tray = new Forms.NotifyIcon { Text = "drawer", Icon = trayIcon, Visible = true };
        tray.MouseUp += (_, e) =>
        {
            if (e.Button is not (Forms.MouseButtons.Left or Forms.MouseButtons.Right)) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (!IsExiting && !menu.IsOpen) menu.IsOpen = true;
            });
        };
    }
    public void OpenSettings()
    {
        if (positionEditor is not null) { positionEditor.Activate(); return; }
        if (settings is null) { settings = new(this); settings.Closed += (_, _) => settings = null; }
        settings.Show(); settings.Activate();
    }
    public void BeginPositionEdit()
    {
        if (positionEditor is not null) { positionEditor.Activate(); return; }
        if (IsTransferring) return;
        foreach (var host in Drawers) host.Window.SuspendForPositionEdit();
        settings?.Hide();
        positionEditor = new(this);
        positionEditor.Closed += (_, _) =>
        {
            positionEditor = null;
            if (IsExiting) return;
            foreach (var host in Drawers) host.Window.ResumeAfterPositionEdit();
            settings?.Show(); settings?.Activate();
        };
        positionEditor.Show();
    }
    public void Save() => SaveState(true);
    private void SaveState(bool includeHistory)
    {
        try { Store.Save(CaptureWorkspace()); CollectUnusedReferences(includeHistory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { foreach (var host in Drawers) host.Window.Notify("保存失败，内容仍保留在当前会话中"); }
    }
    private void CollectUnusedReferences(bool includeHistory = true)
    {
        if (IsTransferring) return;
        try
        {
            var retained = new HashSet<string>(Transfer.RetainedReferenceIds, StringComparer.OrdinalIgnoreCase);
            foreach (var host in Drawers) retained.UnionWith(host.Session.GetRetainedReferenceIds(includeHistory));
            Store.CollectReferences(CaptureWorkspace(), retained);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    public void ExitApp()
    {
        if (IsTransferring) { Edge.Notify("请先结束拖放再退出"); return; }
        foreach (var host in Drawers) host.Window.Canvas.CommitEdit();
        SaveState(false); IsExiting = true; Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        shortcuts?.Dispose(); tray?.Dispose(); trayIcon?.Dispose(); activateTimer?.Stop(); activateRequest?.Dispose(); mutex?.Dispose();
        base.OnExit(e);
    }
}
