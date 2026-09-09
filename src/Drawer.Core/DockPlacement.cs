namespace Drawer.Core;

public readonly record struct DockBounds(double X, double Y, double Width, double Height);

// Position is a fraction of the handle's available travel, so saved positions adapt to resolution changes.
public readonly record struct DockPlacement(DockEdge Edge, double Position)
{
    public const double HandleLength = 132;
    public const double HandleThickness = 24;
    public const double HintThickness = 7;
    public DockBounds HandleBounds(double width, double height) => Bounds(width, height,
        Edge == DockEdge.Top ? HandleLength : HandleThickness, Edge == DockEdge.Top ? HandleThickness : HandleLength);
    public static bool Overlaps(DockBounds a, DockBounds b) =>
        a.X < b.X + b.Width + 8 - .000001 && a.X + a.Width + 8 - .000001 > b.X &&
        a.Y < b.Y + b.Height + 8 - .000001 && a.Y + a.Height + 8 - .000001 > b.Y;
    public DockPlacement? AvoidOverlap(IEnumerable<DockPlacement> others, double width, double height)
    {
        var blocked = others.Select(p => p.HandleBounds(width, height)).ToArray();
        double travel = Math.Max(0, (Edge == DockEdge.Top ? width : height) - HandleLength);
        var positions = new List<double> { Position, 0, 1 };
        if (travel > 0)
            foreach (var b in blocked)
            {
                double start = Edge == DockEdge.Top ? b.X : b.Y;
                double end = start + (Edge == DockEdge.Top ? b.Width : b.Height);
                positions.Add(Math.Clamp((start - HandleLength - 8) / travel, 0, 1));
                positions.Add(Math.Clamp((end + 8) / travel, 0, 1));
            }
        double desiredPosition = Position;
        foreach (double position in positions.Distinct().OrderBy(p => Math.Abs(p - desiredPosition)))
        {
            var candidate = new DockPlacement(Edge, position);
            if (blocked.All(b => !Overlaps(candidate.HandleBounds(width, height), b))) return candidate;
        }
        return null;
    }
    public static DockPlacement From(Preferences preferences) => new(preferences.Edge, preferences.EdgePosition);

    public DockBounds Bounds(double screenWidth, double screenHeight, double width, double height)
    {
        width = Math.Clamp(width, 0, screenWidth);
        height = Math.Clamp(height, 0, screenHeight);
        double length = Edge == DockEdge.Top ? screenWidth : screenHeight;
        double handle = Math.Min(HandleLength, length);
        double center = handle / 2 + Math.Max(0, length - handle) * Math.Clamp(Position, 0, 1);
        return Edge switch
        {
            DockEdge.Left => new(0, Math.Clamp(center - height / 2, 0, screenHeight - height), width, height),
            DockEdge.Right => new(screenWidth - width, Math.Clamp(center - height / 2, 0, screenHeight - height), width, height),
            _ => new(Math.Clamp(center - width / 2, 0, screenWidth - width), 0, width, height)
        };
    }

    public DockPlacement Drag(double x, double y, double screenWidth, double screenHeight, double grabOffset = 0)
    {
        x = Math.Clamp(x, 0, screenWidth);
        y = Math.Clamp(y, 0, screenHeight);
        double Distance(DockEdge edge) => edge switch { DockEdge.Left => x, DockEdge.Right => screenWidth - x, _ => y };
        var nearest = new[] { DockEdge.Top, DockEdge.Left, DockEdge.Right }.MinBy(Distance);
        // A competing edge must be clearly closer; equal distances retain the current edge.
        var edge = Distance(nearest) + 24 < Distance(Edge) ? nearest : Edge;
        double length = edge == DockEdge.Top ? screenWidth : screenHeight;
        double coordinate = edge == DockEdge.Top ? x : y;
        if (edge == Edge) coordinate -= grabOffset;
        double travel = Math.Max(0, length - HandleLength);
        return new(edge, travel == 0 ? .5 : Math.Clamp((coordinate - HandleLength / 2) / travel, 0, 1));
    }
}

public sealed class DockPlacementEdit(Preferences preferences)
{
    public DockPlacement Original { get; } = DockPlacement.From(preferences);
    public DockPlacement Draft { get; set; } = DockPlacement.From(preferences);

    // Persist a detached state first. A failed save leaves both the live preferences and original position intact.
    public void Commit(DrawerState state, Action<DrawerState> persist)
    {
        var candidate = DrawerState.Deserialize(state.Serialize());
        candidate.Preferences.Edge = Draft.Edge;
        candidate.Preferences.EdgePosition = Draft.Position;
        persist(candidate);
        state.Preferences.Edge = Draft.Edge;
        state.Preferences.EdgePosition = Draft.Position;
    }
}
