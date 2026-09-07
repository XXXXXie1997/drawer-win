using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Drawer.Windows;

public partial class App : Application
{
    private Mutex? mutex;
    private EventWaitHandle? activateRequest;
    private System.Windows.Threading.DispatcherTimer? activateTimer;
    private Forms.NotifyIcon? tray;
    private SettingsWindow? settings;
    public PersistenceStore Store { get; private set; } = null!;
    public DrawerSession Session { get; private set; } = null!;
    public EdgeWindow Edge { get; private set; } = null!;
    public TransferService Transfer { get; private set; } = null!;
    public FileReferenceService Files { get; private set; } = null!;
    public bool InspectionMode { get; private set; }
    public bool IsExiting { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Packaging verification runs without creating windows, touching clipboard or user data.
        if (e.Args.Length == 2 && e.Args[0] == "--verify-package")
        {
            Shutdown(PackageVerification.Run(e.Args[1]));
            return;
        }
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
            var loaded = Store.Load();
            Session = new(loaded.State);
            Files = new(root);
            Transfer = new(Store, Files);
            Edge = new(this);
            MainWindow = Edge;
            Session.Committed += Save;
            Edge.Show();
            CreateTray();
            activateTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
            activateTimer.Tick += (_, _) => { if (activateRequest.WaitOne(0)) Edge.OpenFocused(); };
            activateTimer.Start();
            try { Store.CollectImages(Session.State); } catch { /* Retry collection at next launch. */ }
            CollectUnusedReferences();
            if (loaded.Notice is not null) { Edge.OpenFocused(); Edge.Notify(loaded.Notice); }
            if (e.Args.Contains("--show") || InspectionMode) Edge.OpenFocused();
            if (e.Args.Contains("--smoke-test"))
            {
                Edge.OpenFocused();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) => { timer.Stop(); ExitApp(); };
                timer.Start();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex is FutureSchemaException ? ex.Message : "drawer 启动失败，请检查数据目录的访问权限。\n" + ex.Message, "drawer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("⚙  偏好设置…", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("⏻  退出 drawer", null, (_, _) => Dispatcher.Invoke(ExitApp));
        tray = new Forms.NotifyIcon { Text = "drawer", Icon = System.Drawing.SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) menu.Show(Forms.Cursor.Position); };
    }
    public void OpenSettings()
    {
        if (settings is null)
        {
            settings = new(this);
            settings.Closed += (_, _) => settings = null;
        }
        settings.Show();
        settings.Activate();
    }
    public void Save() => SaveState(includeHistory: true);
    private void SaveState(bool includeHistory)
    {
        try
        {
            Store.Save(Session.State);
            CollectUnusedReferences(includeHistory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Edge.Notify("保存失败，内容仍保留在当前会话中");
        }
    }
    private void CollectUnusedReferences(bool includeHistory = true)
    {
        if (Edge.IsTransferring) return;
        try
        {
            var retained = Session.GetRetainedReferenceIds(includeHistory);
            retained.UnionWith(Transfer.RetainedReferenceIds);
            Store.CollectReferences(Session.State, retained);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { /* Resource cleanup can retry later without treating a successful save as a failure. */ }
    }
    public void ExitApp()
    {
        if (Edge.IsTransferring) { Edge.Notify("请先结束拖放再退出"); return; }
        Edge.Canvas.CommitEdit();
        SaveState(includeHistory: false);
        IsExiting = true;
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        activateTimer?.Stop();
        activateRequest?.Dispose();
        mutex?.Dispose();
        base.OnExit(e);
    }
}
