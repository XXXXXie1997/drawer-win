using System.Security.Cryptography;
using System.Text;

namespace Drawer.Core;

public sealed class PersistenceStore
{
    public string Root { get; }
    public string ImageDirectory => Path.Combine(Root, "images");
    public string ReferenceDirectory => Path.Combine(Root, "references");
    public string StatePath => Path.Combine(Root, "state.json");
    public string BackupPath => Path.Combine(Root, "state.backup.json");
    public bool ReadOnly { get; private set; }
    public PersistenceStore(string root)
    {
        Root = root;
        Directory.CreateDirectory(ImageDirectory);
    }
    public (DrawerState State, string? Notice) Load() => LoadState(DrawerState.Deserialize, () => new DrawerState());
    public (DrawerWorkspace State, string? Notice) LoadWorkspace() => LoadState(DrawerWorkspace.Deserialize, () => new DrawerWorkspace());
    private (T State, string? Notice) LoadState<T>(Func<string, T> deserialize, Func<T> create)
    {
        foreach (string path in new[] { StatePath, BackupPath })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var state = deserialize(File.ReadAllText(path));
                if (path == BackupPath)
                {
                    // Quarantine bad primary; the next save must not replace the valid backup with it.
                    if (File.Exists(StatePath)) File.Move(StatePath, StatePath + ".corrupt-" + DateTime.UtcNow.Ticks);
                    return (state, "主状态损坏，已从备份恢复");
                }
                return (state, null);
            }
            catch (FutureSchemaException) { ReadOnly = true; throw; }
            catch (Exception e) when (e is InvalidDataException or IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
        }
        bool corrupt = File.Exists(StatePath) || File.Exists(BackupPath);
        if (corrupt)
        {
            foreach (var p in new[] { StatePath, BackupPath })
                if (File.Exists(p)) File.Move(p, p + ".corrupt-" + DateTime.UtcNow.Ticks);
        }
        return (create(), corrupt ? "状态恢复失败，已保留损坏文件并使用空抽屉" : null);
    }
    public void Save(DrawerState state) => SaveJson(state.Serialize());
    public void Save(DrawerWorkspace state) { state.Validate(); SaveJson(state.Serialize()); }
    private void SaveJson(string json)
    {
        if (ReadOnly) throw new IOException("数据处于只读保护状态");
        string temp = StatePath + ".tmp";
        var bytes = Encoding.UTF8.GetBytes(json);
        using (var f = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            f.Write(bytes);
            f.Flush(true);
        }
        if (File.Exists(StatePath)) File.Replace(temp, StatePath, BackupPath);
        else File.Move(temp, StatePath);
    }
    public string PutImage(byte[] png)
    {
        string id = Convert.ToHexStringLower(SHA256.HashData(png));
        string path = ImagePath(id);
        if (!File.Exists(path))
        {
            string temp = path + ".tmp";
            using (var f = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { f.Write(png); f.Flush(true); }
            File.Move(temp, path, true);
        }
        return id;
    }
    public string ImagePath(string id)
    {
        if (id.Length != 64 || id.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("无效图片标识");
        return Path.Combine(ImageDirectory, id + ".png");
    }
    // Called only at startup, when no undo stack or active transfer exists.
    public void CollectImages(DrawerState current) => CollectImages(current.Items);
    public void CollectImages(DrawerWorkspace current) => CollectImages(current.Items);
    private void CollectImages(IEnumerable<DrawerItem> current)
    {
        var ids = current.Select(i => i.ResourceId).OfType<string>().ToHashSet();
        if (File.Exists(BackupPath))
        {
            try { ids.UnionWith(DrawerWorkspace.Deserialize(File.ReadAllText(BackupPath)).Items.Select(i => i.ResourceId).OfType<string>()); }
            catch { return; } // An unreadable backup might still be recoverable; preserve resources.
        }
        if (Directory.EnumerateFiles(Root, "*.corrupt-*").Any()) return;
        foreach (string f in Directory.EnumerateFiles(ImageDirectory, "*.png"))
            if (!ids.Contains(Path.GetFileNameWithoutExtension(f))) File.Delete(f);
    }

    public int CollectReferences(DrawerState current, IEnumerable<string>? retainedIds = null) => CollectReferences(current.Items, retainedIds);
    public int CollectReferences(DrawerWorkspace current, IEnumerable<string>? retainedIds = null) => CollectReferences(current.Items, retainedIds);
    private int CollectReferences(IEnumerable<DrawerItem> current, IEnumerable<string>? retainedIds)
    {
        if (ReadOnly || !Directory.Exists(ReferenceDirectory)) return 0;
        // Never traverse a user-replaced references directory or recursively delete anything.
        if ((File.GetAttributes(ReferenceDirectory) & FileAttributes.ReparsePoint) != 0) return 0;
        if (Directory.EnumerateFiles(Root, "*.corrupt-*").Any()) return 0;
        var ids = new HashSet<string>(retainedIds ?? [], StringComparer.OrdinalIgnoreCase);
        void Retain(IEnumerable<DrawerItem> items) => ids.UnionWith(items.Where(i => i.Kind == ItemKind.FileReference)
            .Select(i => i.ReferenceId).OfType<string>());
        Retain(current);
        // Include both durable states. A save failure must never destroy a recoverable reference.
        foreach (string path in new[] { StatePath, BackupPath })
        {
            if (!File.Exists(path)) continue;
            try { Retain(DrawerWorkspace.Deserialize(File.ReadAllText(path)).Items); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
            { return 0; }
        }
        int removed = 0;
        foreach (string path in Directory.EnumerateFiles(ReferenceDirectory, "*.lnk", SearchOption.TopDirectoryOnly))
        {
            string id = Path.GetFileNameWithoutExtension(path);
            // Only GUID-named links created by drawer belong to this resource collector.
            if (!Guid.TryParseExact(id, "N", out _) || ids.Contains(id)) continue;
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                File.Delete(path); // Deletes the .lnk file, never resolves or deletes its target.
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Retry next save. */ }
        }
        return removed;
    }
}
