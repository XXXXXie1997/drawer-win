using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Drawer.Core;

public sealed class DrawerRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "1";
    public DrawerState State { get; set; } = new();
}

public sealed class DrawerWorkspace
{
    public const int MaximumDrawers = 5;
    public const int MaximumLabelCharacters = 8;
    public int SchemaVersion { get; set; } = 2;
    public string SettingsPage { get; set; } = "基础";
    public List<DrawerRecord> Drawers { get; set; } = [new()];
    [JsonIgnore] public IEnumerable<DrawerItem> Items => Drawers.SelectMany(d => d.State.Items);
    public string Serialize() => JsonSerializer.Serialize(this, DrawerState.JsonOptions);
    public static DrawerWorkspace Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        int schema = document.RootElement.TryGetProperty("SchemaVersion", out var version) ? version.GetInt32() : 1;
        if (schema > 2) throw new FutureSchemaException();
        if (schema <= 1)
        {
            var legacy = DrawerState.Deserialize(json);
            return new() { SettingsPage = legacy.Preferences.SettingsPage, Drawers = [new() { State = legacy }] };
        }
        var workspace = JsonSerializer.Deserialize<DrawerWorkspace>(json, DrawerState.JsonOptions) ?? throw new InvalidDataException("抽屉列表为空");
        workspace.Validate();
        return workspace;
    }
    public void Validate()
    {
        if (SchemaVersion != 2 || Drawers is null || Drawers.Count is < 1 or > MaximumDrawers ||
            Drawers.Any(d => d is null || d.Id == Guid.Empty || d.State is null) ||
            Drawers.Select(d => d.Id).Distinct().Count() != Drawers.Count)
            throw new InvalidDataException("抽屉列表无效");
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var drawer in Drawers)
        {
            // Existing releases accepted 12 characters; keep old workspaces readable.
            string label = NormalizeLabel(drawer.Label, 12);
            if (!labels.Add(label)) throw new InvalidDataException("抽屉标识不能重复");
            drawer.Label = label;
            DrawerState.Deserialize(drawer.State.Serialize());
        }
    }
    public static string NormalizeLabel(string value)
        => NormalizeLabel(value, MaximumLabelCharacters);
    private static string NormalizeLabel(string value, int maximumCharacters)
    {
        if (value is null) throw new InvalidDataException("请输入抽屉标识");
        string label;
        try { label = value.Trim().Normalize(NormalizationForm.FormC); }
        catch (ArgumentException ex) { throw new InvalidDataException("抽屉标识包含无效字符，请重新输入", ex); }
        if (label.Length == 0 || label.Any(char.IsControl)) throw new InvalidDataException("抽屉标识不能为空或包含换行");
        if (new StringInfo(label).LengthInTextElements > maximumCharacters) throw new InvalidDataException($"抽屉标识最多 {maximumCharacters} 个字符（支持文字和 emoji）");
        return label;
    }
    public DrawerRecord Add(DockPlacement placement)
    {
        if (Drawers.Count >= MaximumDrawers) throw new InvalidDataException("最多支持 5 个抽屉");
        string label = Enumerable.Range(1, MaximumDrawers + 1).Select(n => n.ToString(CultureInfo.InvariantCulture))
            .First(name => Drawers.All(d => !string.Equals(d.Label, name, StringComparison.OrdinalIgnoreCase)));
        var record = new DrawerRecord { Label = label };
        record.State.Preferences.Edge = placement.Edge;
        record.State.Preferences.EdgePosition = placement.Position;
        Drawers.Add(record);
        return record;
    }
    public void Rename(Guid id, string label)
    {
        label = NormalizeLabel(label);
        if (Drawers.Any(d => d.Id != id && string.Equals(d.Label, label, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("抽屉标识不能重复");
        Drawers.Single(d => d.Id == id).Label = label;
    }
    public void Remove(Guid id)
    {
        if (Drawers.Count == 1) throw new InvalidDataException("至少保留一个抽屉");
        Drawers.Remove(Drawers.Single(d => d.Id == id));
    }
    public DrawerWorkspace WithPlacements(IReadOnlyDictionary<Guid, DockPlacement> placements)
    {
        if (placements.Count != Drawers.Count || Drawers.Any(d => !placements.ContainsKey(d.Id)))
            throw new InvalidDataException("抽屉位置列表不完整");
        var candidate = Deserialize(Serialize());
        foreach (var drawer in candidate.Drawers)
        {
            var placement = placements[drawer.Id];
            drawer.State.Preferences.Edge = placement.Edge;
            drawer.State.Preferences.EdgePosition = placement.Position;
        }
        candidate.Validate();
        return candidate;
    }
}
