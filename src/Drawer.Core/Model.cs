using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace Drawer.Core;

public enum ItemKind { Text, FileReference, EmbeddedImage }
public enum DockEdge { Top, Left, Right }
public enum DrawerMode { Hidden, Handle, Preview, Focused }
public readonly record struct Cell(int X, int Y)
{
    public static Cell operator +(Cell a, Cell b) => new(a.X + b.X, a.Y + b.Y);
    public static Cell operator -(Cell a, Cell b) => new(a.X - b.X, a.Y - b.Y);
}
public readonly record struct Footprint(int Width, int Height);
public readonly record struct GridFrame(Cell Origin, Footprint Size)
{
    [JsonIgnore] public int Right => Origin.X + Size.Width;
    [JsonIgnore] public int Bottom => Origin.Y + Size.Height;
    public bool Intersects(GridFrame b) => Origin.X < b.Right && Right > b.Origin.X && Origin.Y < b.Bottom && Bottom > b.Origin.Y;
    public GridFrame Translate(Cell delta) => new(Origin + delta, Size);
}
public sealed class DrawerItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ItemKind Kind { get; set; }
    public GridFrame Frame { get; set; }
    public string Text { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Path { get; set; } = "";
    public string? ReferenceId { get; set; }
    public string? ResourceId { get; set; }
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
}
public sealed class Viewport
{
    public double OffsetX { get; set; } = 32;
    public double OffsetY { get; set; } = 32;
    public double Zoom { get; set; } = 1;
}
public sealed class Preferences
{
    public DockEdge Edge { get; set; }
    public bool SelectionMustContain { get; set; }
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 560;
    public string SettingsPage { get; set; } = "基础";
}
public sealed class DrawerState
{
    public const int CurrentSchema = 1;
    public int SchemaVersion { get; set; } = CurrentSchema;
    public List<DrawerItem> Items { get; set; } = [];
    public Cell? InsertionPoint { get; set; }
    public Viewport Viewport { get; set; } = new();
    public Preferences Preferences { get; set; } = new();
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);
    public static DrawerState Deserialize(string json)
    {
        var state = JsonSerializer.Deserialize<DrawerState>(json, JsonOptions) ?? throw new InvalidDataException("状态为空");
        if (state.SchemaVersion > CurrentSchema) throw new FutureSchemaException();
        if (state.SchemaVersion < 1 || state.Items is null || state.Viewport is null || state.Preferences is null)
            throw new InvalidDataException("状态格式无效");
        if (state.Items.Any(i => i is null || !Enum.IsDefined(i.Kind) || i.Frame.Size.Width < 1 || i.Frame.Size.Height < 1 ||
                i.Frame.Size.Width > 100_000 || i.Frame.Size.Height > 100_000 ||
                Math.Abs((long)i.Frame.Origin.X) > 10_000_000 || Math.Abs((long)i.Frame.Origin.Y) > 10_000_000 ||
                i.Text is null || i.Path is null || i.DisplayName is null) ||
            state.Items.Select(i => i.Id).Distinct().Count() != state.Items.Count)
            throw new InvalidDataException("对象数据无效");
        if (!double.IsFinite(state.Viewport.Zoom) || state.Viewport.Zoom < .35 || state.Viewport.Zoom > 2.5 ||
            !double.IsFinite(state.Viewport.OffsetX) || !double.IsFinite(state.Viewport.OffsetY) ||
            !double.IsFinite(state.Preferences.Width) || !double.IsFinite(state.Preferences.Height) || !Enum.IsDefined(state.Preferences.Edge))
            throw new InvalidDataException("视口数据无效");
        return state;
    }
}
public sealed class FutureSchemaException : IOException
{
    public FutureSchemaException() : base("此数据由较新版本创建，请使用更新版本打开。") { }
}
public static class ItemSizing
{
    public const double Unit = 28;
    public static Footprint Image(int width, int height)
    {
        double ratio = width / (double)Math.Max(1, height);
        return ratio < .75 ? new(3, 4) : ratio <= 1.33 ? new(3, 3) : ratio <= 2.4 ? new(4, 3) : new(5, 3);
    }
    public static Footprint Text(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int Columns(string s) => StringInfo.GetTextElementEnumerator(s) is var e ? Count(e) : 0;
        int Count(TextElementEnumerator e)
        {
            int n = 0;
            while (e.MoveNext()) { var s = e.GetTextElement(); n += s[0] == '\t' ? 4 : s[0] >= 0x2E80 ? 2 : 1; }
            return n;
        }
        int width = Math.Clamp((int)Math.Ceiling(lines.Max(Columns) / 3d), 2, 6);
        int rows = lines.Sum(s => Math.Max(1, (int)Math.Ceiling(Columns(s) / (width * 3d))));
        return new(width, Math.Max(1, (int)Math.Ceiling((rows * 20 + 6) / 28d)));
    }
}
