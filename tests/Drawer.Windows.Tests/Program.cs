using Drawer.Core;
using Drawer.Windows;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "drawer-transfer-test-" + Guid.NewGuid().ToString("N"));
        var tests = new List<(string, Action)>();
        tests.Add(("Startup is opt-in, quotes portable paths and preserves disabled state", StartupChecks.Run));
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var store = new PersistenceStore(root);
        var files = new FileReferenceService(root);
        var transfer = new TransferService(store, files);
        typeof(App).GetProperty(nameof(App.Store))!.SetValue(app, store);
        typeof(App).GetProperty(nameof(App.Files))!.SetValue(app, files);
        tests.Add(("Unified settings save commits label and appearance atomically", () =>
        {
            var host = new DrawerHost(app, new DrawerRecord { Label = "原名称" }); app.Drawers.Add(host);
            try
            {
                string before = app.CaptureWorkspace().Serialize();
                bool invalid = app.ConfigureDrawer(host.Id, p => DrawerSizing.SetPercentages(p, 95, 20), out _, "新名称");
                Assert(!invalid && app.CaptureWorkspace().Serialize() == before, "Invalid size also leaves the label unchanged");
                bool saved = app.ConfigureDrawer(host.Id, p => { DrawerSizing.SetPercentages(p, 70, 20); p.Theme = DrawerTheme.Forest; p.SelectionMustContain = true; }, out _, "新名称");
                var loaded = store.LoadWorkspace().State.Drawers.Single();
                Assert(saved && host.Label == "新名称" && loaded.Label == host.Label, "Label saved to disk and live state together");
                Assert(loaded.State.Preferences.WidthPercent == 70 && loaded.State.Preferences.Theme == DrawerTheme.Forest && loaded.State.Preferences.SelectionMustContain, "All pages committed in the same save");
            }
            finally { host.Window.CloseDrawer(); app.Drawers.Clear(); }
        }));
        // A wide PNG catches accidental reuse of downsampled thumbnail data during export.
        var bitmap = BitmapSource.Create(1200, 600, 96, 96, PixelFormats.Bgra32, null, new byte[1200 * 600 * 4], 1200 * 4);
        var input = new DataObject(DataFormats.Bitmap, bitmap);
        var image = transfer.Read(input).Single();
        tests.Add(("Clipboard screenshots with unused alpha remain visible while PNG transparency is preserved", () =>
        {
            byte[] pixels = [30, 60, 120, 0, 90, 150, 210, 0];
            var screenshot = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
            var imported = transfer.Read(new DataObject(DataFormats.Bitmap, screenshot)).Single();
            var loaded = new FormatConvertedBitmap(transfer.LoadImage(imported, true)!, PixelFormats.Bgra32, null, 0);
            var actual = new byte[8]; loaded.CopyPixels(actual, 8, 0);
            Assert(actual[3] == 255 && actual[7] == 255 && actual[0] == 30 && actual[6] == 210, "Opaque screenshot retains original RGB pixels after save and reload");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(screenshot));
            using var stream = new MemoryStream(); encoder.Save(stream); stream.Position = 0;
            var transparentData = new DataObject(); transparentData.SetData("PNG", stream); transparentData.SetData(DataFormats.Bitmap, screenshot);
            var transparent = transfer.Read(transparentData).Single();
            var transparentLoaded = new FormatConvertedBitmap(transfer.LoadImage(transparent, true)!, PixelFormats.Bgra32, null, 0);
            transparentLoaded.CopyPixels(actual, 8, 0);
            Assert(actual[3] == 0 && actual[7] == 0, "Explicit PNG alpha is preserved and takes precedence over bitmap fallback");
        }));
        var text = new DrawerItem { Kind = ItemKind.Text, Text = "hello 中文", Frame = new(default, new(4, 1)) };
        tests.Add(("Theme palettes keep text readable on tray and handle", () =>
        {
            static double Luminance(Brush brush)
            {
                var c = ((SolidColorBrush)brush).Color;
                static double Linear(byte value) { double v = value / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
                return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
            }
            static double Contrast(Brush a, Brush b) => (Math.Max(Luminance(a), Luminance(b)) + .05) / (Math.Min(Luminance(a), Luminance(b)) + .05);
            foreach (var theme in Enum.GetValues<DrawerTheme>())
            {
                var palette = DrawerPalette.For(theme);
                Assert(Contrast(palette.Ink, palette.Surface) >= 4.5 && Contrast(palette.Ink, palette.Shell) >= 4.5, "Text readable in every theme");
                Assert(Contrast(palette.Accent, palette.Surface) >= 3, "Insertion point visible in every theme");
            }
        }));
        tests.Add(("Handle labels stay upright and keep compound emoji intact", () =>
        {
            var label = new HandleLabel();
            foreach (var edge in Enum.GetValues<DockEdge>())
            {
                label.Update("文👨‍👩‍👧‍👦A", edge);
                Assert(label.LayoutTransform.Value.IsIdentity && label.RenderTransform.Value.IsIdentity, "No label rotation on any edge");
                Assert(label.Text == (edge == DockEdge.Top ? "文👨‍👩‍👧‍👦A" : "文\n👨‍👩‍👧‍👦\nA"), "Side layout preserves grapheme clusters");
            }
            label.Update("一二三四五六七八九十", DockEdge.Left);
            Assert(label.Text.Split('\n').Length == 8 && label.Text.EndsWith("…"), "Long side label fits handle with ellipsis");
            label.Update("横向名称", DockEdge.Top);
            Assert(label.Text == "横向名称", "Returning to top restores one line");
        }));
        tests.Add(("Invalid rename returns feedback through the app without mutating drawers", () =>
        {
            // Construct the actual app boundary without startup, windows, hotkeys or user-data access.
            var first = new DrawerHost(app, new DrawerRecord { Label = "原名称" });
            var second = new DrawerHost(app, new DrawerRecord { Label = "另一个" });
            app.Drawers.Add(first); app.Drawers.Add(second);
            try
            {
                string before = app.CaptureWorkspace().Serialize();
                foreach (string invalid in new[] { new string('长', 10000), "另一个", "  ", "\uD800" })
                {
                    bool success = app.ChangeDrawers(w => w.Rename(first.Id, invalid), out string? error);
                    Assert(!success && !string.IsNullOrWhiteSpace(error), "Invalid label is reported without escaping the app boundary");
                    Assert(app.CaptureWorkspace().Serialize() == before, "All drawer data remains unchanged");
                }
                var workspace = app.CaptureWorkspace();
                workspace.Rename(first.Id, string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦", 8)));
                Assert(workspace.Drawers[0].Label.Contains("👨‍👩‍👧‍👦"), "Eight visible emoji accepted regardless of UTF-16 length");
            }
            finally { first.Window.CloseDrawer(); second.Window.CloseDrawer(); app.Drawers.Clear(); }
        }));
        tests.Add(("Animated trays stay attached, preserve host bounds and leave transparent margins", () => AnimationChecks.Run(app)));
        tests.Add(("Cross-drawer drag preserves mixed types and rejects only its source drawer", () =>
        {
            Guid source = Guid.NewGuid(), target = Guid.NewGuid();
            var output = transfer.Build([text, image], true, source);
            Assert(transfer.IsFromDrawer(output.Data, source) && !transfer.IsFromDrawer(output.Data, target), "Drag can enter another drawer but not duplicate into source");
            var incoming = transfer.Read(output.Data);
            Assert(incoming.Count == 2 && incoming[0].Text == text.Text && incoming[1].ResourceId == image.ResourceId, "Cross-drawer copy preserves text and image resource");
            Assert(incoming.All(i => i.Id != text.Id && i.Id != image.Id), "Target objects get independent identities");
        }));
        tests.Add(("Screenshot import and lossless export", () =>
        {
            Assert(image.Kind == ItemKind.EmbeddedImage && image.PixelWidth == 1200, "Import records original size");
            var output = transfer.Build([image], false);
            var exported = (BitmapSource)output.Data.GetData(DataFormats.Bitmap)!;
            Assert(exported.PixelWidth == 1200 && exported.PixelHeight == 600, "Full-resolution bitmap output");
            Assert(output.Data.GetDataPresent("PNG"), "Lossless PNG representation available");
        }));
        tests.Add(("Mixed internal roundtrip preserves objects and identity semantics", () =>
        {
            var output = transfer.Build([text, image], false);
            var incoming = transfer.Read(output.Data);
            Assert(incoming.Count == 2 && incoming[0].Kind == ItemKind.Text && incoming[1].Kind == ItemKind.EmbeddedImage, "Mixed types preserved");
            Assert(incoming[0].Id != text.Id && incoming[1].ResourceId == image.ResourceId, "New identity, shared immutable image");
        }));
        tests.Add(("Real files take priority over path text", () =>
        {
            string source = Path.Combine(root, "source.txt"); File.WriteAllText(source, "unchanged");
            var data = new DataObject(); data.SetData(DataFormats.FileDrop, new[] { source }); data.SetText(source);
            var incoming = transfer.Read(data);
            Assert(incoming.Count == 1 && incoming[0].Kind == ItemKind.FileReference, "No duplicate text object");
            Assert(files.Resolve(incoming[0]) == source && incoming[0].ReferenceId is not null, "Shell reference created and resolves");
            Assert(File.ReadAllText(source) == "unchanged", "Source is untouched");
        }));
        tests.Add(("Missing file is excluded from successful output IDs", () =>
        {
            var missing = new DrawerItem { Kind = ItemKind.FileReference, Path = Path.Combine(root, "missing"), Frame = new(default, new(3, 3)) };
            var result = transfer.Build([missing, text], false);
            Assert(result.Exported.SetEquals([text.Id]), "Only successful items eligible for cut removal");
        }));
        tests.Add(("Shortcut garbage collection never deletes the original file", () =>
        {
            var referenceStore = new PersistenceStore(Path.Combine(root, "reference-cleanup"));
            var references = new FileReferenceService(referenceStore.Root);
            string source = Path.Combine(referenceStore.Root, "keep-source.txt");
            File.WriteAllText(source, "original file stays");
            var removed = references.Create(source);
            Assert(removed.ReferenceId is not null, "Created a real Shell shortcut");
            string link = Path.Combine(referenceStore.ReferenceDirectory, removed.ReferenceId + ".lnk");
            Assert(referenceStore.CollectReferences(new DrawerState()) == 1 && !File.Exists(link), "Orphan Shell shortcut removed");
            Assert(File.ReadAllText(source) == "original file stays", "Shortcut target remains unchanged");
        }));
        tests.Add(("Virtual PNG descriptor and delayed stream", () =>
        {
            var output = transfer.Build([image], true);
            Assert(transfer.IsOwn(output.Data), "Self-drag marker survives COM wrapper");
            Assert(!output.Data.GetDataPresent(DataFormats.FileDrop), "Pure screenshot drag uses virtual files");
            var com = (ComDataObject)output.Data;
            var descriptor = Format("FileGroupDescriptorW", TYMED.TYMED_HGLOBAL);
            com.GetData(ref descriptor, out var medium);
            try
            {
                nint ptr = GlobalLock(medium.unionmember);
                try
                {
                    Assert(Marshal.ReadInt32(ptr) == 1, "One descriptor");
                    string name = Marshal.PtrToStringUni(ptr + 4 + 72)!;
                    Assert(name == "drawer-image-" + image.Id.ToString("N")[..8] + ".png", "Friendly export filename");
                }
                finally { GlobalUnlock(medium.unionmember); }
            }
            finally { ReleaseStgMedium(ref medium); }
            var contents = Format("FileContents", TYMED.TYMED_ISTREAM, 0);
            com.GetData(ref contents, out medium);
            try
            {
                var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
                try
                {
                    byte[] signature = new byte[8]; stream.Read(signature, 8, 0);
                    Assert(signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Stream contains real PNG");
                }
                finally { Marshal.ReleaseComObject(stream); }
            }
            finally { ReleaseStgMedium(ref medium); }
            contents.lindex = 10;
            Assert(com.QueryGetData(ref contents) < 0, "Out-of-range stream rejected");
        }));
        int failures = 0;
        foreach (var (name, action) in tests)
        {
            try { action(); Console.WriteLine("PASS " + name); }
            catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e); }
        }
        Console.WriteLine($"{tests.Count - failures}/{tests.Count} passed");
        Directory.Delete(root, true);
        return failures == 0 ? 0 : 1;
    }
    private static FORMATETC Format(string name, TYMED medium, int index = -1) => new() { cfFormat = unchecked((short)DataFormats.GetDataFormat(name).Id), dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = index, tymed = medium };
    private static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
}
