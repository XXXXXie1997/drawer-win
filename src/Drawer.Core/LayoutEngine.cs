namespace Drawer.Core;

public static class LayoutEngine
{
    // Every Manhattan ring has one canonical clockwise traversal, beginning on the right.
    public static IEnumerable<Cell> Candidates(Cell desired, int maxRadius = 128)
    {
        yield return desired;
        for (int r = 1; r <= maxRadius; r++)
        {
            for (int i = 0; i < r; i++) yield return desired + new Cell(r - i, i);
            for (int i = 0; i < r; i++) yield return desired + new Cell(-i, r - i);
            for (int i = 0; i < r; i++) yield return desired + new Cell(-r + i, -i);
            for (int i = 0; i < r; i++) yield return desired + new Cell(i, -r + i);
        }
    }
    public static Cell FindNearestOrigin(Cell desired, Footprint size, IReadOnlyList<GridFrame> occupied)
    {
        foreach (var p in Candidates(desired))
            if (!occupied.Any(f => f.Intersects(new(p, size)))) return p;
        return new(Math.Max(desired.X, occupied.Count == 0 ? desired.X : occupied.Max(f => f.Right)), desired.Y);
    }
    public static Cell FindNearestTranslation(IReadOnlyList<GridFrame> frames, Cell desired, IReadOnlyList<GridFrame> occupied)
    {
        foreach (var d in Candidates(desired))
            if (!frames.Any(f => occupied.Any(o => o.Intersects(f.Translate(d))))) return d;
        return new((occupied.Count == 0 ? 0 : occupied.Max(f => f.Right)) - frames.Min(f => f.Origin.X), desired.Y);
    }
    public static void Reflow(List<DrawerItem> items, Guid anchorId)
    {
        var anchor = items.First(i => i.Id == anchorId);
        // Reserve unaffected objects first, so only collisions and necessary displacement move.
        var occupied = new List<GridFrame> { anchor.Frame };
        var displaced = new List<DrawerItem>();
        foreach (var i in items.Where(i => i.Id != anchorId))
        {
            if (occupied.Any(o => o.Intersects(i.Frame))) displaced.Add(i);
            else occupied.Add(i.Frame);
        }
        foreach (var i in displaced)
        {
            i.Frame = new(FindNearestOrigin(i.Frame.Origin, i.Frame.Size, occupied), i.Frame.Size);
            occupied.Add(i.Frame);
        }
    }
}
