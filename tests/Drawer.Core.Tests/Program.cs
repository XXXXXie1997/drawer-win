using Drawer.Core;

var tests = new (string Name, Action Body)[]
{
    ("Touching edges and negative cells", () =>
    {
        var a = new GridFrame(new(-3, -3), new(3, 3));
        Assert(!a.Intersects(new(new(0, -3), new(3, 3))), "Edges are not overlap");
        Assert(a.Intersects(new(new(-1, -1), new(3, 3))), "Negative cells collide");
    }),
    ("Deterministic placement of 500 mixed objects", () =>
    {
        List<GridFrame> Run()
        {
            var frames = new List<GridFrame>();
            var random = new Random(42);
            for (int i = 0; i < 500; i++)
            {
                var size = new Footprint(random.Next(2, 7), random.Next(1, 6));
                var origin = LayoutEngine.FindNearestOrigin(new(random.Next(-25, 26), random.Next(-25, 26)), size, frames);
                var frame = new GridFrame(origin, size);
                Assert(!frames.Any(f => f.Intersects(frame)), "New object cannot overlap");
                frames.Add(frame);
            }
            return frames;
        }
        Assert(Run().SequenceEqual(Run()), "Repeated inputs must match");
    }),
    ("Group motion preserves relative positions", () =>
    {
        var session = new DrawerSession(new());
        var a = Item(new(0, 0)); var b = Item(new(5, 0)); var c = Item(new(0, 5));
        session.State.Items.AddRange([a, b, c]);
        session.Selection.UnionWith([a.Id, b.Id]);
        session.Move(new(0, 5));
        Assert(b.Frame.Origin - a.Frame.Origin == new Cell(5, 0), "Relative layout preserved");
        Assert(!a.Frame.Intersects(c.Frame) && !b.Frame.Intersects(c.Frame), "Group avoids stationary item");
    }),
    ("Editing reflow pins anchor and is reversible", () =>
    {
        var session = new DrawerSession(new());
        var a = Item(default); var b = Item(new(3, 0)); var c = Item(new(10, 0));
        session.State.Items.AddRange([a, b, c]);
        session.Apply(s => { a.Text = "Expanded"; a.Frame = new(default, new(6, 4)); LayoutEngine.Reflow(s.Items, a.Id); });
        Assert(a.Frame.Origin == default, "Edited anchor does not move");
        Assert(!a.Frame.Intersects(b.Frame) && !c.Frame.Intersects(b.Frame), "Stable layout has no overlap");
        Assert(c.Frame.Origin == new Cell(10, 0), "Unrelated object stays put");
        session.Undo();
        Assert(session.State.Items[1].Frame.Origin == new Cell(3, 0), "One undo restores reflow");
        session.Redo();
        Assert(session.State.Items[0].Text == "Expanded", "Redo restores text");
    }),
    ("Batch insert and removal are single undo steps", () =>
    {
        var session = new DrawerSession(new());
        session.Insert([Item(default), Item(default), Item(default)], default);
        session.Undo(); Assert(session.State.Items.Count == 0, "Batch undo");
        session.Redo(); Assert(session.State.Items.Count == 3, "Batch redo");
        session.Remove(session.State.Items.Select(i => i.Id).ToArray());
        session.Undo(); Assert(session.State.Items.Count == 3, "Clear undo");
    }),
    ("Atomic state, backup recovery and image reachability", () => WithStore(store =>
    {
        string keep = store.PutImage([1, 2, 3]), orphan = store.PutImage([9, 8, 7]);
        var state = new DrawerState();
        state.Items.Add(new() { Kind = ItemKind.EmbeddedImage, Frame = new(default, new(3, 3)), ResourceId = keep });
        store.Save(state); state.Items.Clear(); store.Save(state);
        store.CollectImages(state);
        Assert(File.Exists(store.ImagePath(keep)), "Backup image retained");
        Assert(!File.Exists(store.ImagePath(orphan)), "Unreferenced image collected");
        File.WriteAllText(store.StatePath, "broken JSON");
        var loaded = store.Load();
        Assert(loaded.State.Items.Count == 1 && loaded.Notice is not null, "Recovered valid backup");
        store.Save(loaded.State);
        Assert(DrawerState.Deserialize(File.ReadAllText(store.BackupPath)).Items.Count == 1, "Corrupt main did not overwrite backup");
    })),
    ("Future schema is never overwritten", () => WithStore(store =>
    {
        File.WriteAllText(store.StatePath, "{\"SchemaVersion\":999}");
        bool rejected = false;
        try { store.Load(); } catch (FutureSchemaException) { rejected = true; }
        Assert(rejected && store.ReadOnly, "Forward-version guard");
        Assert(File.ReadAllText(store.StatePath).Contains("999"), "Original survives");
    })),
    ("Invalid state and image paths are rejected", () => WithStore(store =>
    {
        bool rejected = false;
        try { store.ImagePath("../../secret"); } catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "No resource traversal");
        rejected = false;
        var s = new DrawerState(); s.Items.Add(Item(default)); s.Items.Add(s.Items[0]);
        try { DrawerState.Deserialize(s.Serialize()); } catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "Duplicate IDs rejected");
    })),
    ("File removal never touches source file", () => WithStore(store =>
    {
        string path = Path.Combine(store.Root, "source.txt"); File.WriteAllText(path, "source unchanged");
        var session = new DrawerSession(new());
        var item = Item(default); item.Kind = ItemKind.FileReference; item.Path = path;
        session.Insert([item], default); session.Remove([item.Id]); session.Undo();
        Assert(File.ReadAllText(path) == "source unchanged", "File remains intact");
    })),
    ("Reference collection retains durable states and ignores unrelated files", () => WithStore(store =>
    {
        string live = Link(store), backup = Link(store), leased = Link(store), orphan = Link(store);
        string foreign = Path.Combine(store.ReferenceDirectory, "my-shortcut.lnk"); File.WriteAllText(foreign, "not owned by drawer");
        var previous = new DrawerState { Items = [Reference(live), Reference(backup)] };
        store.Save(previous);
        var current = new DrawerState { Items = [Reference(live.ToUpperInvariant())] };
        store.Save(current);
        Assert(store.CollectReferences(current, [leased]) == 1, "Only the unreferenced managed link is collected");
        Assert(Exists(store, live) && Exists(store, backup) && Exists(store, leased), "Current, backup and leases retained");
        Assert(!Exists(store, orphan) && File.Exists(foreign), "Unknown files left alone");
    })),
    ("Reference collection preserves undo and redo then releases discarded history", () => WithStore(store =>
    {
        string id = Link(store);
        var session = new DrawerSession(new());
        session.Insert([Reference(id)], default);
        session.Undo();
        store.Save(session.State); store.Save(session.State);
        store.CollectReferences(session.State, session.GetRetainedReferenceIds());
        Assert(Exists(store, id), "Redo still needs the file reference");
        session.Redo();
        session.Remove(session.State.Items.Select(i => i.Id).ToArray());
        store.Save(session.State); store.Save(session.State);
        store.CollectReferences(session.State, session.GetRetainedReferenceIds());
        Assert(Exists(store, id), "Undo still needs the file reference");
        session.Undo(); session.Undo(); // Undo removal, then undo insertion: file now exists only in redo.
        session.Insert([Item(default)], default); // Branching drops the redo history.
        store.Save(session.State); store.Save(session.State);
        Assert(store.CollectReferences(session.State, session.GetRetainedReferenceIds()) == 1, "Discarded redo releases link");
    })),
    ("Expired undo entries no longer pin reference files", () => WithStore(store =>
    {
        string id = Link(store);
        var session = new DrawerSession(new() { Items = [Reference(id), Item(new(5, 0))] });
        session.Remove([session.State.Items[0].Id]);
        for (int i = 0; i < 101; i++)
        {
            string value = i.ToString(); session.Apply(s => s.Items[0].Text = value);
        }
        store.Save(session.State); store.Save(session.State);
        Assert(store.CollectReferences(session.State, session.GetRetainedReferenceIds()) == 1, "History limit releases old links");
    })),
    ("Session exit releases undo-only references and startup collects old leaks", () => WithStore(store =>
    {
        string oldLeak = Link(store), removed = Link(store), live = Link(store);
        var session = new DrawerSession(new() { Items = [Reference(removed), Reference(live)] });
        session.Remove([session.State.Items[0].Id]);
        store.Save(session.State); store.Save(session.State);
        Assert(store.CollectReferences(session.State, session.GetRetainedReferenceIds()) == 1, "Existing legacy orphan removed during use");
        Assert(!Exists(store, oldLeak) && Exists(store, removed), "Undo reference remains in session");
        Assert(store.CollectReferences(session.State, session.GetRetainedReferenceIds(false)) == 1, "Exit releases undo-only reference");
        Assert(Exists(store, live), "Current file reference always survives");
    })),
    ("Shared reference is retained until its last live owner is removed", () => WithStore(store =>
    {
        string id = Link(store);
        var state = new DrawerState { Items = [Reference(id), Reference(id)] };
        state.Items.RemoveAt(0);
        store.Save(state); store.Save(state);
        Assert(store.CollectReferences(state) == 0 && Exists(store, id), "Removing one copy keeps shared link");
        state.Items.Clear(); store.Save(state); store.Save(state);
        Assert(store.CollectReferences(state) == 1, "Removing final owner releases link after backup advances");
    })),
    ("Unreadable or quarantined recovery data suspends reference deletion", () => WithStore(store =>
    {
        string id = Link(store);
        var empty = new DrawerState(); store.Save(empty); store.Save(empty);
        File.WriteAllText(store.BackupPath, "broken JSON");
        Assert(store.CollectReferences(empty) == 0 && Exists(store, id), "Unreadable backup protects possibly recoverable link");
        File.WriteAllText(store.BackupPath, empty.Serialize());
        File.WriteAllText(store.StatePath + ".corrupt-1", "recoverable later");
        Assert(store.CollectReferences(empty) == 0 && Exists(store, id), "Quarantined data protects resources");
    }))
};
int failures = 0;
foreach (var test in tests)
{
    try { test.Body(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception e) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + e.Message); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static DrawerItem Item(Cell origin) => new() { Kind = ItemKind.Text, Text = "test", Frame = new(origin, new(3, 3)) };
static DrawerItem Reference(string id) => new() { Kind = ItemKind.FileReference, ReferenceId = id, Frame = new(default, new(3, 3)) };
static string Link(PersistenceStore store)
{
    Directory.CreateDirectory(store.ReferenceDirectory);
    string id = Guid.NewGuid().ToString("N");
    File.WriteAllText(Path.Combine(store.ReferenceDirectory, id + ".lnk"), "test link resource");
    return id;
}
static bool Exists(PersistenceStore store, string id) => File.Exists(Path.Combine(store.ReferenceDirectory, id + ".lnk"));
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static void WithStore(Action<PersistenceStore> action)
{
    string root = Path.Combine(Path.GetTempPath(), "drawer-test-" + Guid.NewGuid().ToString("N"));
    try { action(new(root)); }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
