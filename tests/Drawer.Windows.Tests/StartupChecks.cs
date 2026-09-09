using Drawer.Windows;
using System.IO;

internal static class StartupChecks
{
    public static void Run()
    {
        var store = new MemoryStore();
        string path = @"C:\我的应用\drawer new\drawer.exe";
        var startup = new StartupRegistration(store, () => path);
        startup.RefreshEnabledPath(); Require(store.Value is null, "Startup must remain opt-in");
        startup.SetEnabled(true); Require(store.Value == "\"" + path + "\"", "Spaces and Unicode path are quoted without show arguments");
        path = @"D:\drawer-next\drawer.exe";
        startup.RefreshEnabledPath(); Require(store.Value == "\"" + path + "\"", "Enabled entry follows the new portable executable");
        var original = store.Value; store.FailWrites = true;
        try { startup.SetEnabled(true); throw new Exception("Expected write failure"); } catch (IOException) { }
        Require(startup.IsEnabled && store.Value == original, "Write failure preserves existing setting");
        store.FailWrites = false; startup.SetEnabled(false);
        startup.RefreshEnabledPath(); Require(!startup.IsEnabled && store.Value is null, "Disabled entry stays disabled after upgrades");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class MemoryStore : IStartupEntryStore
    {
        public string? Value;
        public bool FailWrites;
        public string? Read() => Value;
        public void Write(string command) { if (FailWrites) throw new IOException("denied"); Value = command; }
        public void Remove() => Value = null;
    }
}
