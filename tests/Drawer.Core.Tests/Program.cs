using Drawer.Core;

var tests = new (string Name, Action Body)[]
{
    ("Percentage sizes persist, adapt to screens and enforce limits", () =>
    {
        var state = new DrawerState();
        DrawerSizing.SetPercentages(state.Preferences, 90, 10);
        var p = DrawerState.Deserialize(state.Serialize()).Preferences;
        Assert(DrawerSizing.Resolve(p, 1920, 1080) == (1728d, 108d), "Percentages persist and allow heights below legacy minimum");
        Assert(DrawerSizing.Resolve(p, 2560, 1440) == (2304d, 144d), "Resolution changes preserve proportions");
        foreach (double invalid in new[] { 9d, 91d, double.NaN, double.PositiveInfinity })
        {
            bool rejected = false;
            try { DrawerSizing.SetPercentages(p, 50, invalid); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected && p.WidthPercent == 90 && p.HeightPercent == 10, "Invalid values leave both dimensions unchanged");
        }
        Assert(DrawerSizing.Percentages(new Preferences { Width = 10000, Height = 160 }, 1920, 1080) == (90d, 15d), "Legacy values convert into slider limits");
        var incomplete = new Preferences { WidthPercent = 50 };
        bool incompleteRejected = false;
        try { DrawerCommands.Validate(incomplete); } catch (InvalidDataException) { incompleteRejected = true; }
        Assert(incompleteRejected, "Incomplete percentage settings are rejected");
    }),
    ("Labels enforce eight graphemes while older long labels remain readable", () =>
    {
        var workspace = new DrawerWorkspace();
        workspace.Drawers[0].Label = "一二三四五六七八九十甲乙";
        var loaded = DrawerWorkspace.Deserialize(workspace.Serialize());
        Assert(loaded.Drawers[0].Label == workspace.Drawers[0].Label, "Upgrade preserves existing long names");
        foreach (string label in new[] { "一二三四五六七八九", "abcdefghi", string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦", 9)) })
        {
            bool rejected = false;
            try { loaded.Rename(loaded.Drawers[0].Id, label); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "New labels over eight visible characters are rejected");
        }
        loaded.Rename(loaded.Drawers[0].Id, string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦", 8)));
        Assert(DrawerWorkspace.Deserialize(loaded.Serialize()).Drawers[0].Label == loaded.Drawers[0].Label, "Compound emoji stay intact after save and reload");
    }),
    ("Spring motion preserves velocity on reversal and matches different frame rates", () =>
    {
        foreach (double damping in new[] { .86, .94, 1d })
        {
            var samples = new List<double>();
            foreach (int fps in new[] { 30, 60, 120, 144 })
            {
                var spring = new SpringValue(); spring.Snap(4); spring.Target = 1600;
                for (int frame = 0; frame < fps / 2; frame++) spring.Step(1d / fps, 24, damping);
                samples.Add(spring.Value);
                double value = spring.Value, velocity = spring.Velocity;
                spring.Target = 4;
                Assert(spring.Value == value && spring.Velocity == velocity, "Retarget never teleports or discards velocity");
                for (int frame = 0; frame < fps; frame++) spring.Step(1d / fps, 30, 1);
                Assert(spring.IsSettled(.001), "Reversal settles onto the hint");
            }
            Assert(samples.Max() - samples.Min() < .000001, "Refresh rate does not affect the trajectory");
        }
        var interrupted = new SpringValue(); interrupted.Snap(4); interrupted.Target = 560;
        interrupted.Step(.06, 24, .86);
        double before = interrupted.Value, speed = interrupted.Velocity;
        interrupted.Target = 4; interrupted.Step(.000001, 30, 1);
        Assert(Math.Abs(interrupted.Value - before) < .01 && Math.Abs(interrupted.Velocity - speed) < 1, "Mid-flight reversal remains continuous");
    }),
    ("Selection moves insertion point to clicked item or group bounds", () =>
    {
        var a = Item(new(-5, 4)); var b = Item(new(6, -2));
        var session = new DrawerSession(new() { Items = [a, b] });
        int saves = 0; session.Committed += () => saves++;
        session.Selection.Add(a.Id); session.UpdateInsertionFromSelection(a.Id);
        Assert(session.State.InsertionPoint == a.Frame.Origin && saves == 1 && !session.CanUndo, "Click moves persisted point without content undo");
        session.Selection.Add(b.Id); session.UpdateInsertionFromSelection(b.Id);
        Assert(session.State.InsertionPoint == b.Frame.Origin, "Additive click uses the clicked object");
        session.UpdateInsertionFromSelection();
        Assert(session.State.InsertionPoint == new Cell(-5, -2), "Marquee or select-all uses group top-left");
        session.Selection.Clear(); session.UpdateInsertionFromSelection();
        Assert(session.State.InsertionPoint == new Cell(-5, -2), "Clearing selection retains useful insertion point");
    }),
    ("Insertion point follows actual collision-adjusted movement and undo", () =>
    {
        var moving = Item(default); var obstacle = Item(new(0, 3));
        var session = new DrawerSession(new() { Items = [moving, obstacle] });
        session.Selection.Add(moving.Id); session.UpdateInsertionFromSelection(moving.Id);
        session.Move(new(0, 3));
        Assert(session.State.InsertionPoint == session.State.Items.Single(i => i.Id == moving.Id).Frame.Origin, "Point follows resolved movement");
        session.Undo(); Assert(session.State.InsertionPoint == new Cell(0, 0), "Undo restores point with layout");
        session.Insert([Item(default)], new(20, 20));
        Assert(session.State.InsertionPoint == session.State.Items.Single(i => session.Selection.Contains(i.Id)).Frame.Origin, "New selected object updates insertion point");
    }),
    ("Appearance and auxiliary shortcuts survive restart independently per drawer", () => WithStore(store =>
    {
        var workspace = new DrawerWorkspace(); var first = workspace.Drawers[0]; var second = workspace.Add(new(DockEdge.Right, .5));
        DrawerCommands.SetSize(first.State.Preferences, 700, 800); first.State.Preferences.Theme = DrawerTheme.Light;
        first.State.Preferences.CommandShortcuts[DrawerCommand.FitAll] = DrawerCommands.Suggest(DrawerCommand.FitAll);
        first.State.Viewport.Zoom = .7; store.Save(workspace);
        var loaded = store.LoadWorkspace().State;
        Assert(loaded.Drawers[0].State.Preferences.Width == 700 && loaded.Drawers[0].State.Preferences.Theme == DrawerTheme.Light && loaded.Drawers[0].State.Viewport.Zoom == .7, "Size and theme do not change canvas zoom");
        Assert(loaded.Drawers[1].State.Preferences.Theme == DrawerTheme.Dark && loaded.Drawers[1].State.Preferences.CommandShortcuts.Count == 0, "Other drawer keeps its own settings");
        Assert(loaded.Drawers[0].State.Preferences.CommandShortcuts[DrawerCommand.FitAll] == DrawerCommands.Suggest(DrawerCommand.FitAll), "Command binding persists");
        var session = new DrawerSession(first.State); session.Insert([Item(default)], default);
        session.State.Preferences.Theme = DrawerTheme.Midnight; session.Undo();
        Assert(session.State.Preferences.Theme == DrawerTheme.Midnight, "Content undo preserves theme");
    })),
    ("Invalid sizes, duplicate commands and reserved editing bindings are rejected", () =>
    {
        foreach (var size in new[] { (159d, 560d), (32769d, 560d), (420d, double.NaN), (420d, 159d), (420d, double.PositiveInfinity) })
        {
            var preferences = new Preferences(); bool rejected = false;
            try { DrawerCommands.SetSize(preferences, size.Item1, size.Item2); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected && preferences.Width == 420 && preferences.Height == 560, "Invalid input leaves original size intact");
        }
        var p = new Preferences(); p.CommandShortcuts[DrawerCommand.FitAll] = new(ShortcutModifiers.Control, 0x43);
        bool invalid = false; try { DrawerCommands.Validate(p); } catch (InvalidDataException) { invalid = true; }
        Assert(invalid, "Cannot reassign Ctrl+C");
        p.CommandShortcuts[DrawerCommand.FitAll] = DrawerCommands.Suggest(DrawerCommand.FitAll);
        p.CommandShortcuts[DrawerCommand.ResetZoom] = p.CommandShortcuts[DrawerCommand.FitAll];
        invalid = false; try { DrawerCommands.Validate(p); } catch (InvalidDataException) { invalid = true; }
        Assert(invalid, "Two commands cannot use the same combination in a drawer");
    }),
    ("Wide and narrow drawer sizes survive restart and adapt without overwriting preferences", () => WithStore(store =>
    {
        var workspace = new DrawerWorkspace();
        var wide = workspace.Drawers[0];
        var narrow = workspace.Add(new(DockEdge.Right, .9));
        DrawerCommands.SetSize(wide.State.Preferences, 3400, 160);
        DrawerCommands.SetSize(narrow.State.Preferences, 160, 2000);
        store.Save(workspace);
        var loaded = store.LoadWorkspace().State;
        Assert(loaded.Drawers[0].State.Preferences.Width == 3400 && loaded.Drawers[0].State.Preferences.Height == 160, "Wide horizontal layout persists");
        Assert(loaded.Drawers[1].State.Preferences.Width == 160 && loaded.Drawers[1].State.Preferences.Height == 2000, "Narrow vertical layout persists");
        foreach (var screen in new[] { (3840d, 2160d), (1920d, 1080d), (1280d, 720d), (120d, 100d) })
        foreach (var record in loaded.Drawers)
        foreach (var edge in Enum.GetValues<DockEdge>())
        {
            var p = record.State.Preferences;
            var size = DrawerSizing.Fit(p.Width, p.Height, screen.Item1, screen.Item2);
            var bounds = new DockPlacement(edge, .9).Bounds(screen.Item1, screen.Item2, size.Width, size.Height);
            Assert(size.Width == Math.Min(p.Width, screen.Item1) && size.Height == Math.Min(p.Height, screen.Item2), "Resolution and scaled display limits apply without the old cap");
            Assert(bounds.X >= 0 && bounds.Y >= 0 && bounds.X + bounds.Width <= screen.Item1 && bounds.Y + bounds.Height <= screen.Item2, "Large drawer remains within every dock edge");
        }
        var full = DrawerSizing.Fit(10000, 10000, 2560, 1440);
        Assert(full == (2560, 1440), "Resizing can reach both screen boundaries");
        Assert(loaded.Drawers[0].State.Preferences.Width == 3400 && loaded.Drawers[1].State.Preferences.Height == 2000, "Smaller display does not overwrite requested dimensions");
    })),
    ("Invalid persisted appearance recovers backup and keeps resources", () => WithStore(store =>
    {
        var workspace = new DrawerWorkspace(); store.Save(workspace); store.Save(workspace);
        string id = Link(store);
        string invalid = workspace.Serialize().Replace("\"Theme\": \"Dark\"", "\"Theme\": 999");
        File.WriteAllText(store.StatePath, invalid);
        Assert(store.CollectReferences(workspace) == 0 && Exists(store, id), "Invalid recovery state suspends resource deletion");
        Assert(store.LoadWorkspace().Notice is not null, "Bad theme settings recover valid backup");
    })),
    ("Independent hotkeys resolve stable drawer IDs and reject conflicts", () =>
    {
        var backend = new FakeShortcutRegistration(); using var shortcuts = new DrawerShortcuts(backend);
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();
        var one = new ShortcutBinding(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x31);
        var two = one with { VirtualKey = 0x32 };
        Assert(shortcuts.TryApply(first, one, () => { }, out _), "First binding registers");
        Assert(shortcuts.TryApply(second, two, () => { }, out _), "Second binding registers");
        Assert(shortcuts.Resolve(backend.Keys.Single(p => p.Value == one).Key) == first, "First key targets first drawer identity");
        Assert(shortcuts.Resolve(backend.Keys.Single(p => p.Value == two).Key) == second, "Second key targets second drawer identity");
        bool saved = false;
        Assert(!shortcuts.TryApply(second, one, () => saved = true, out var duplicate) && duplicate is not null && !saved, "Duplicate does not reach persistence");
        var blocked = one with { VirtualKey = 0x33 }; backend.Blocked.Add(blocked);
        Assert(!shortcuts.TryApply(first, blocked, () => saved = true, out _) && !saved, "OS conflict leaves saved data unchanged");
        Assert(shortcuts.GetActive(first) == one && shortcuts.GetActive(second) == two && backend.Keys.Count == 2, "Both original bindings remain available");
    }),
    ("Hotkey save failure retains old registration and discards replacement", () =>
    {
        var backend = new FakeShortcutRegistration(); using var shortcuts = new DrawerShortcuts(backend);
        Guid id = Guid.NewGuid(); var old = new ShortcutBinding(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x44);
        shortcuts.TryApply(id, old, () => { }, out _); int oldId = backend.Keys.Single().Key;
        foreach (Exception failure in new Exception[] { new IOException("full"), new UnauthorizedAccessException(), new InvalidDataException("invalid") })
        {
            Assert(!shortcuts.TryApply(id, old with { VirtualKey = 0x45 }, () => throw failure, out var error) && error is not null, "Failed save reports error");
            Assert(backend.Keys.Count == 1 && shortcuts.Resolve(oldId) == id && shortcuts.GetActive(id) == old, "Old key is never released on failure");
        }
        Assert(!shortcuts.TryApply(id, null, () => throw new IOException(), out _), "Failed disable save also rolls back");
        Assert(shortcuts.Resolve(oldId) == id, "Disable failure retains old key");
    }),
    ("Replacement, clearing and deletion release keys without stale dispatch", () =>
    {
        var backend = new FakeShortcutRegistration(); var shortcuts = new DrawerShortcuts(backend);
        Guid id = Guid.NewGuid(); var binding = new ShortcutBinding(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x31);
        shortcuts.TryApply(id, binding, () => { }, out _); int oldId = backend.Keys.Single().Key;
        shortcuts.TryApply(id, binding with { VirtualKey = 0x32 }, () => Assert(backend.Keys.Count == 2, "Old key retained until durable save succeeds"), out _);
        Assert(shortcuts.Resolve(oldId) is null && backend.Keys.Count == 1, "Queued old messages are ignored");
        shortcuts.TryApply(id, null, () => { }, out _);
        Assert(backend.Keys.Count == 0 && shortcuts.GetActive(id) is null, "Clearing releases key");
        shortcuts.TryApply(id, binding, () => { }, out _); int deleteId = backend.Keys.Single().Key;
        shortcuts.Remove(id); shortcuts.TryApply(Guid.NewGuid(), binding, () => { }, out _);
        Assert(shortcuts.Resolve(deleteId) is null, "Deleting then reusing key cannot redirect queued messages");
        shortcuts.Dispose(); Assert(backend.Keys.Count == 0, "Exit releases all registrations");
    }),
    ("Shortcut rules preserve editing commands and settings survive rename and undo", () => WithStore(store =>
    {
        var modifiers = ShortcutModifiers.Control | ShortcutModifiers.Alt;
        foreach (var invalid in new[] { new ShortcutBinding(0, 0x41), new(ShortcutModifiers.Control, 0x43), new(ShortcutModifiers.Control | ShortcutModifiers.Shift, 0x5A), new(modifiers, 0x7B), new(ShortcutModifiers.Alt, 0x73), new((ShortcutModifiers)8, 0x44) })
            Assert(!invalid.IsValid, "Reserved or unmodified shortcut rejected");
        var workspace = DrawerWorkspace.Deserialize("{\"SchemaVersion\":1}");
        Assert(workspace.Drawers[0].State.Preferences.OpenShortcut is null, "Older drawers start without grabbing a key");
        var first = workspace.Drawers[0]; var second = workspace.Add(new(DockEdge.Left, .5));
        first.State.Preferences.OpenShortcut = new(modifiers, 0x31); second.State.Preferences.OpenShortcut = new(modifiers, 0x32);
        var session = new DrawerSession(first.State); session.Insert([Item(default)], default);
        session.State.Preferences.OpenShortcut = new(modifiers, 0x33); session.Undo(); first.State = session.State;
        workspace.Rename(first.Id, "工作📌"); store.Save(workspace);
        var restored = store.LoadWorkspace().State;
        Assert(restored.Drawers.Single(d => d.Id == first.Id).State.Preferences.OpenShortcut?.VirtualKey == 0x33, "Rename and content undo keep binding on same drawer");
        Assert(restored.Drawers.Single(d => d.Id == second.Id).State.Preferences.OpenShortcut?.VirtualKey == 0x32, "Other drawer binding unaffected");
    })),
    ("Single drawer migrates without losing content, placement or original backup", () => WithStore(store =>
    {
        var legacy = new DrawerState { Items = [Item(new(-4, 8))] };
        legacy.Preferences.Edge = DockEdge.Left; legacy.Preferences.EdgePosition = .23;
        legacy.Viewport.Zoom = .8; store.Save(legacy);
        var workspace = store.LoadWorkspace().State;
        Assert(workspace.Drawers.Count == 1 && workspace.Drawers[0].Label == "1", "Legacy becomes first drawer");
        var original = workspace.Drawers[0];
        Assert(original.State.Serialize() == legacy.Serialize(), "All legacy content and preferences preserved");
        store.Save(workspace);
        Assert(DrawerState.Deserialize(File.ReadAllText(store.BackupPath)).Items.Count == 1, "Original durable state backed up");
        Assert(store.LoadWorkspace().State.Drawers[0].Id == original.Id, "Drawer identity stable after restart");
        bool guarded = false;
        try { new PersistenceStore(store.Root).Load(); } catch (FutureSchemaException) { guarded = true; }
        Assert(guarded, "Old single-drawer reader cannot overwrite multi-drawer data");
    })),
    ("Drawer limits and Unicode labels are validated consistently", () =>
    {
        var workspace = new DrawerWorkspace();
        workspace.Rename(workspace.Drawers[0].Id, " 工作📌 ");
        Assert(workspace.Drawers[0].Label == "工作📌", "Trim label and accept emoji");
        for (int i = 1; i < 5; i++) workspace.Add(new(DockEdge.Top, i / 5d));
        Assert(workspace.Drawers.Select(d => d.Label).Distinct().Count() == 5, "New labels avoid existing names");
        Reject(() => workspace.Add(new(DockEdge.Top, .5)));
        Reject(() => workspace.Rename(workspace.Drawers[1].Id, "工作📌"));
        Reject(() => workspace.Rename(workspace.Drawers[1].Id, " \n "));
        workspace.Rename(workspace.Drawers[1].Id, "é");
        Reject(() => workspace.Rename(workspace.Drawers[2].Id, "e\u0301"));
        workspace.Rename(workspace.Drawers[1].Id, "Work");
        Reject(() => workspace.Rename(workspace.Drawers[2].Id, "work"));
        foreach (var id in workspace.Drawers.Skip(1).Select(d => d.Id).ToArray()) workspace.Remove(id);
        Reject(() => workspace.Remove(workspace.Drawers[0].Id));
        static void Reject(Action action)
        {
            bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Invalid drawer mutation rejected");
        }
    }),
    ("Multiple drawer sessions keep undo and content independent across restart", () => WithStore(store =>
    {
        var workspace = new DrawerWorkspace(); workspace.Add(new(DockEdge.Right, .3));
        var first = new DrawerSession(workspace.Drawers[0].State); var second = new DrawerSession(workspace.Drawers[1].State);
        first.Insert([Item(default)], default); second.Insert([Item(new(8, 8))], new(8, 8));
        first.Undo(); Assert(second.State.Items.Count == 1 && second.CanUndo, "Undo does not touch second drawer");
        workspace.Drawers[0].State = first.State; workspace.Drawers[1].State = second.State;
        store.Save(workspace); var restored = store.LoadWorkspace().State;
        Assert(restored.Drawers[0].State.Items.Count == 0 && restored.Drawers[1].State.Items.Count == 1, "Each drawer persists its own state");
    })),
    ("All placements save in one workspace while canceled drafts stay isolated", () => WithStore(store =>
    {
        var workspace = new DrawerWorkspace(); workspace.Add(new(DockEdge.Left, .5)); store.Save(workspace);
        var placements = workspace.Drawers.ToDictionary(d => d.Id, _ => new DockPlacement(DockEdge.Right, .8));
        var draft = workspace.WithPlacements(placements);
        Assert(workspace.Drawers[0].State.Preferences.Edge == DockEdge.Top && store.LoadWorkspace().State.Drawers[1].State.Preferences.Edge == DockEdge.Left, "Preview does not mutate original or disk");
        store.Save(draft);
        Assert(store.LoadWorkspace().State.Drawers.All(d => d.State.Preferences.Edge == DockEdge.Right), "Both placements committed together");
        File.WriteAllText(store.StatePath, "broken JSON");
        Assert(store.LoadWorkspace().State.Drawers[1].State.Preferences.Edge == DockEdge.Left, "Backup restores complete previous workspace");
    })),
    ("Handle placement finds free space and avoids corner collisions", () =>
    {
        foreach (var screen in new[] { (1920d, 1080d), (800d, 600d), (320d, 240d) })
        {
            var placed = new List<DockPlacement>();
            for (int i = 0; i < (screen.Item1 < 640 ? 3 : 5); i++)
            {
                DockPlacement? next = null;
                foreach (var edge in Enum.GetValues<DockEdge>())
                {
                    next = new DockPlacement(edge, .5).AvoidOverlap(placed, screen.Item1, screen.Item2);
                    if (next is not null) break;
                }
                var available = next ?? throw new Exception($"Expected free handle space on {screen}, drawer {i + 1}");
                Assert(placed.All(p => !DockPlacement.Overlaps(available.HandleBounds(screen.Item1, screen.Item2), p.HandleBounds(screen.Item1, screen.Item2))), "Handles have separation");
                placed.Add(available);
            }
            var topCorner = new DockPlacement(DockEdge.Top, 0);
            var left = new DockPlacement(DockEdge.Left, 0).AvoidOverlap([topCorner], screen.Item1, screen.Item2);
            Assert(left is not null && !DockPlacement.Overlaps(topCorner.HandleBounds(screen.Item1, screen.Item2), left.Value.HandleBounds(screen.Item1, screen.Item2)), "Cross-edge corner also avoids overlap");
        }
    }),
    ("Resource collection accounts for all drawers, removed drawers and backup", () => WithStore(store =>
    {
        string live = Link(store), removed = Link(store), orphan = Link(store);
        string png = store.PutImage([1, 2, 3]);
        var workspace = new DrawerWorkspace();
        var other = workspace.Add(new(DockEdge.Left, .5));
        other.State.Items.Add(Reference(live)); other.State.Items.Add(new() { Kind = ItemKind.EmbeddedImage, ResourceId = png, Frame = new(default, new(3, 3)) });
        workspace.Drawers[0].State.Items.Add(Reference(removed));
        store.Save(workspace); workspace.Remove(workspace.Drawers[0].Id); store.Save(workspace);
        store.CollectImages(workspace);
        Assert(File.Exists(store.ImagePath(png)), "Image in another drawer retained");
        Assert(store.CollectReferences(workspace) == 1 && !Exists(store, orphan), "Only actual orphan removed");
        Assert(Exists(store, live) && Exists(store, removed), "Surviving drawer and deleted-drawer backup retained");
        store.Save(workspace);
        Assert(store.CollectReferences(workspace) == 1 && Exists(store, live), "Deleted drawer releases references after backup advances");
    })),
    ("Old preferences retain centered docking and reject invalid positions", () =>
    {
        var old = DrawerState.Deserialize("{\"Preferences\":{\"Edge\":\"Left\"}}");
        Assert(old.Preferences.EdgePosition == .5, "Missing position migrates to center");
        foreach (double invalid in new[] { -.1, 1.1 })
        {
            old.Preferences.EdgePosition = invalid;
            bool rejected = false;
            try { DrawerState.Deserialize(old.Serialize()); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Out-of-range anchors rejected");
        }
    }),
    ("Dock bounds stay inside small screens and corners throughout animation", () =>
    {
        foreach (var edge in Enum.GetValues<DockEdge>())
        foreach (double fraction in new[] { 0d, .1, .5, .9, 1 })
        foreach (var screen in new[] { (1920d, 1080d), (1280d, 720d), (320d, 240d) })
        foreach (double size in new[] { 1d, 15, 132, 420, 560, 1100, 2100 })
        {
            var bounds = new DockPlacement(edge, fraction).Bounds(screen.Item1, screen.Item2, size, size);
            Assert(bounds.X >= 0 && bounds.Y >= 0 && bounds.X + bounds.Width <= screen.Item1 && bounds.Y + bounds.Height <= screen.Item2, "Visible shell stays on screen");
            Assert(edge switch { DockEdge.Left => bounds.X == 0, DockEdge.Right => bounds.X + bounds.Width == screen.Item1, _ => bounds.Y == 0 }, "Always attached to selected edge");
        }
        Assert(new DockPlacement(DockEdge.Top, .5).Bounds(1920, 1080, 420, 560).X == 750, "Centered old layout unchanged");
    }),
    ("Dragging crosses edges with hysteresis and preserves grab offset", () =>
    {
        var top = new DockPlacement(DockEdge.Top, .5);
        Assert(top.Drag(10, 20, 1920, 1080).Edge == DockEdge.Top, "Near-corner jitter retains top");
        var left = top.Drag(2, 100, 1920, 1080);
        Assert(left.Edge == DockEdge.Left, "Deliberate drag switches to left");
        Assert(left.Drag(20, 10, 1920, 1080).Edge == DockEdge.Left, "Reverse jitter retains left");
        Assert(left.Drag(100, 2, 1920, 1080).Edge == DockEdge.Top, "Can return to top");
        Assert(top.Drag(1918, 400, 1920, 1080).Edge == DockEdge.Right, "Can switch to right");
        Assert(top.Drag(980, 0, 1920, 1080, 20).Position == .5, "Pressing off center does not cause a jump");
        Assert(left.Drag(-100, 2000, 1920, 1080).Position == 1, "Outside drag clamps to end");
    }),
    ("Position editing is isolated, commits durably, and leaves content undo intact", () => WithStore(store =>
    {
        var session = new DrawerSession(new());
        session.Insert([Item(default)], default);
        store.Save(session.State);
        string before = File.ReadAllText(store.StatePath);
        var canceled = new DockPlacementEdit(session.State.Preferences) { Draft = new(DockEdge.Left, .1) };
        Assert(DockPlacement.From(session.State.Preferences) == canceled.Original && File.ReadAllText(store.StatePath) == before, "Discarding a preview changes neither state nor disk");
        var confirmed = new DockPlacementEdit(session.State.Preferences) { Draft = new(DockEdge.Right, .8) };
        confirmed.Commit(session.State, store.Save);
        Assert(DockPlacement.From(store.Load().State.Preferences) == confirmed.Draft, "Restart restores confirmed placement");
        session.Undo();
        Assert(session.State.Items.Count == 0 && DockPlacement.From(session.State.Preferences) == confirmed.Draft, "Content undo does not undo settings");
    })),
    ("Failed placement save retains original live and durable state", () => WithStore(store =>
    {
        var state = new DrawerState(); store.Save(state);
        var edit = new DockPlacementEdit(state.Preferences) { Draft = new(DockEdge.Left, .2) };
        bool failed = false;
        try { edit.Commit(state, _ => throw new IOException("disk full")); } catch (IOException) { failed = true; }
        Assert(failed && DockPlacement.From(state.Preferences) == edit.Original && DockPlacement.From(store.Load().State.Preferences) == edit.Original, "Failure does not commit preview");
    })),
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

sealed class FakeShortcutRegistration : IShortcutRegistration
{
    public Dictionary<int, ShortcutBinding> Keys { get; } = [];
    public HashSet<ShortcutBinding> Blocked { get; } = [];
    public bool Register(int id, ShortcutBinding binding)
    {
        if (Blocked.Contains(binding) || Keys.ContainsValue(binding)) return false;
        Keys.Add(id, binding); return true;
    }
    public void Unregister(int id) => Keys.Remove(id);
}
