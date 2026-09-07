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
        var store = new PersistenceStore(root);
        var files = new FileReferenceService(root);
        var transfer = new TransferService(store, files);
        // A wide PNG catches accidental reuse of downsampled thumbnail data during export.
        var bitmap = BitmapSource.Create(1200, 600, 96, 96, PixelFormats.Bgra32, null, new byte[1200 * 600 * 4], 1200 * 4);
        var input = new DataObject(DataFormats.Bitmap, bitmap);
        var image = transfer.Read(input).Single();
        var text = new DrawerItem { Kind = ItemKind.Text, Text = "hello 中文", Frame = new(default, new(4, 1)) };
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
            Assert(referenceStore.CollectReferences(new()) == 1 && !File.Exists(link), "Orphan Shell shortcut removed");
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
