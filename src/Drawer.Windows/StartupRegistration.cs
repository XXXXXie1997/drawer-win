using Microsoft.Win32;

namespace Drawer.Windows;

public interface IStartupEntryStore
{
    string? Read();
    void Write(string command);
    void Remove();
}

public sealed class StartupRegistration(IStartupEntryStore store, Func<string> executablePath)
{
    public StartupRegistration() : this(new RegistryStore(), CurrentExecutable) { }
    public bool IsEnabled => !string.IsNullOrWhiteSpace(store.Read());
    public void SetEnabled(bool enabled)
    {
        if (enabled) store.Write(Command(executablePath()));
        else store.Remove();
    }
    public void RefreshEnabledPath()
    {
        var existing = store.Read();
        if (string.IsNullOrWhiteSpace(existing)) return;
        var command = Command(executablePath());
        if (!string.Equals(existing, command, StringComparison.OrdinalIgnoreCase)) store.Write(command);
    }
    private static string Command(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Contains('"') || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请使用打包后的 drawer.exe 设置开机自启。");
        return "\"" + path + "\"";
    }
    private static string CurrentExecutable()
    {
        var path = Environment.ProcessPath;
        if (path is null || !string.Equals(Path.GetFileName(path), "drawer.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请使用打包后的 drawer.exe 设置开机自启。");
        return path;
    }
    private sealed class RegistryStore : IStartupEntryStore
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public string? Read() { using var key = Registry.CurrentUser.OpenSubKey(KeyPath); return key?.GetValue("drawer") as string; }
        public void Write(string command) { using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true); key.SetValue("drawer", command, RegistryValueKind.String); }
        public void Remove() { using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true); key?.DeleteValue("drawer", false); }
    }
}
