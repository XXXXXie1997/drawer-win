namespace Drawer.Core;

public sealed class DrawerSession(DrawerState initial)
{
    private sealed record HistoryEntry(string Json, HashSet<string> ReferenceIds);
    private readonly List<HistoryEntry> undo = [];
    private readonly List<HistoryEntry> redo = [];
    public DrawerState State { get; private set; } = initial;
    public HashSet<Guid> Selection { get; } = [];
    public event Action? Changed;
    public event Action? Committed;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    private static HashSet<string> ReferenceIds(DrawerState state) => state.Items
        .Where(i => i.Kind == ItemKind.FileReference).Select(i => i.ReferenceId).OfType<string>()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> GetRetainedReferenceIds(bool includeHistory = true)
    {
        var ids = ReferenceIds(State);
        if (includeHistory)
        {
            foreach (var entry in undo) ids.UnionWith(entry.ReferenceIds);
            foreach (var entry in redo) ids.UnionWith(entry.ReferenceIds);
        }
        return ids;
    }
    public void Refresh() => Changed?.Invoke();
    public void Save() => Committed?.Invoke();
    public void Apply(Action<DrawerState> change)
    {
        string before = State.Serialize();
        try { change(State); }
        catch { State = DrawerState.Deserialize(before); throw; }
        Commit(before);
    }
    public void Commit(string before)
    {
        if (before != State.Serialize())
        {
            undo.Add(new(before, ReferenceIds(DrawerState.Deserialize(before))));
            if (undo.Count > 100) undo.RemoveAt(0);
            redo.Clear();
            Save();
        }
        Refresh();
    }
    public void Insert(IEnumerable<DrawerItem> incoming, Cell origin)
    {
        var batch = incoming.ToList();
        if (batch.Count == 0) return;
        Apply(s =>
        {
            var occupied = s.Items.Select(i => i.Frame).ToList();
            foreach (var item in batch)
            {
                item.Frame = new(LayoutEngine.FindNearestOrigin(origin, item.Frame.Size, occupied), item.Frame.Size);
                s.Items.Add(item);
                occupied.Add(item.Frame);
            }
            Selection.Clear();
            Selection.UnionWith(batch.Select(i => i.Id));
        });
    }
    public void Remove(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        Apply(s => s.Items.RemoveAll(i => set.Contains(i.Id)));
        Selection.ExceptWith(set);
        Refresh();
    }
    public void Move(Cell delta)
    {
        if (delta == default || Selection.Count == 0) return;
        Apply(s =>
        {
            var selected = s.Items.Where(i => Selection.Contains(i.Id)).ToList();
            var other = s.Items.Where(i => !Selection.Contains(i.Id)).Select(i => i.Frame).ToList();
            var actual = LayoutEngine.FindNearestTranslation(selected.Select(i => i.Frame).ToList(), delta, other);
            foreach (var i in selected) i.Frame = i.Frame.Translate(actual);
        });
    }
    public void Undo() => Restore(undo, redo);
    public void Redo() => Restore(redo, undo);
    private void Restore(List<HistoryEntry> source, List<HistoryEntry> target)
    {
        if (source.Count == 0) return;
        target.Add(new(State.Serialize(), ReferenceIds(State)));
        var preferences = State.Preferences;
        State = DrawerState.Deserialize(source[^1].Json);
        State.Preferences = preferences; // Preferences are not part of content undo.
        source.RemoveAt(source.Count - 1);
        Selection.Clear();
        Refresh();
        Save();
    }
}
