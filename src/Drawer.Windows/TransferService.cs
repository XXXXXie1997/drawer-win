using System.Windows;
using System.Windows.Media.Imaging;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

namespace Drawer.Windows;

public sealed class TransferService(PersistenceStore store, FileReferenceService files)
{
    public const string SessionFormat = "drawer.source-session.v1";
    public const string ObjectsFormat = "drawer.objects.v1";
    public string Token { get; } = Guid.NewGuid().ToString();
    private HashSet<string> clipboardReferences = new(StringComparer.OrdinalIgnoreCase);
    private uint clipboardSequence;
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    public IEnumerable<string> RetainedReferenceIds
    {
        get
        {
            uint current = GetClipboardSequenceNumber();
            // A zero result cannot prove that the clipboard was replaced; preserve conservatively.
            if (current != 0 && clipboardSequence != 0 && current != clipboardSequence) clipboardReferences.Clear();
            return clipboardReferences;
        }
    }
    public bool IsOwn(IDataObject data) => data.GetDataPresent(SessionFormat) && data.GetData(SessionFormat) as string == Token;
    public bool CanRead(IDataObject data) => data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text);
    public List<DrawerItem> Read(IDataObject data)
    {
        // Internal copies preserve type and every selected object, even for mixed selections.
        if (data.GetData(ObjectsFormat) is string own && own.StartsWith(Token + "\n", StringComparison.Ordinal))
        {
            var copies = DrawerState.Deserialize(own[(Token.Length + 1)..]).Items;
            foreach (var copy in copies) copy.Id = Guid.NewGuid();
            return copies;
        }
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths)
            return paths.Where(p => File.Exists(p) || Directory.Exists(p)).Select(files.Create).ToList();
        BitmapSource? bitmap = null;
        if (data.GetDataPresent("PNG"))
        {
            object value = data.GetData("PNG");
            Stream? stream = value is byte[] bytes ? new MemoryStream(bytes) : value as Stream;
            if (stream is not null)
            {
                if (stream.CanSeek) stream.Position = 0;
                bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            }
        }
        bitmap ??= data.GetData(DataFormats.Bitmap) as BitmapSource;
        if (bitmap is not null)
        {
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            png.Save(stream);
            return [new DrawerItem { Kind = ItemKind.EmbeddedImage, ResourceId = store.PutImage(stream.ToArray()), PixelWidth = bitmap.PixelWidth, PixelHeight = bitmap.PixelHeight, Frame = new(default, ItemSizing.Image(bitmap.PixelWidth, bitmap.PixelHeight)) }];
        }
        string? text = data.GetData(DataFormats.UnicodeText) as string ?? data.GetData(DataFormats.Text) as string;
        if (!string.IsNullOrWhiteSpace(text)) return [new DrawerItem { Kind = ItemKind.Text, Text = text, Frame = new(default, ItemSizing.Text(text)) }];
        return [];
    }
    public BitmapSource? LoadImage(DrawerItem item, bool fullResolution = false)
    {
        try
        {
            string? path = item.Kind == ItemKind.EmbeddedImage ? store.ImagePath(item.ResourceId ?? "") : files.Resolve(item);
            if (path is null || !File.Exists(path)) return null;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            if (!fullResolution) bitmap.DecodePixelWidth = 512;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }
    public (DataObject Data, HashSet<Guid> Exported) Build(IEnumerable<DrawerItem> selection, bool dragging)
    {
        var items = selection.ToList();
        var data = new DataObject();
        var exported = new HashSet<Guid>();
        var paths = new StringCollection();
        var texts = new List<string>();
        var virtualFiles = new List<VirtualFile>();
        BitmapSource? firstImage = null;
        foreach (var i in items)
        {
            try
            {
                switch (i.Kind)
                {
                    case ItemKind.Text:
                        texts.Add(i.Text); exported.Add(i.Id); break;
                    case ItemKind.FileReference:
                        string? path = files.Resolve(i);
                        if (path is not null) { paths.Add(path); exported.Add(i.Id); }
                        break;
                    case ItemKind.EmbeddedImage:
                        if (i.ResourceId is null) break;
                        string imagePath = store.ImagePath(i.ResourceId);
                        if (!File.Exists(imagePath)) break;
                        // Existing managed PNG is a safe compatibility fallback for mixed transfers.
                        // No new permanent temporary files are created on drag start.
                        paths.Add(imagePath);
                        virtualFiles.Add(new("drawer-image-" + i.Id.ToString("N")[..8] + ".png", imagePath));
                        firstImage ??= LoadImage(i, true);
                        exported.Add(i.Id);
                        break;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException) { }
        }
        if (texts.Count > 0) data.SetText(string.Join(Environment.NewLine, texts), TextDataFormat.UnicodeText);
        bool imagesOnly = virtualFiles.Count > 0 && paths.Count == virtualFiles.Count;
        if (paths.Count > 0 && !(dragging && imagesOnly)) data.SetFileDropList(paths);
        if (firstImage is not null)
        {
            data.SetImage(firstImage);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(firstImage));
            var png = new MemoryStream(); encoder.Save(png); png.Position = 0;
            data.SetData("PNG", png);
        }
        data.SetData(ObjectsFormat, Token + "\n" + new DrawerState { Items = items.Where(i => exported.Contains(i.Id)).ToList() }.Serialize());
        if (dragging) data.SetData(SessionFormat, Token);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1))); // COPY only.
        return (dragging && imagesOnly ? new DataObject(new VirtualFileDataObject(data, virtualFiles)) : data, exported);
    }
    public HashSet<Guid> Copy(IEnumerable<DrawerItem> selection)
    {
        var items = selection.ToList();
        var output = Build(items, false);
        if (output.Exported.Count > 0)
        {
            Clipboard.SetDataObject(output.Data, true);
            clipboardReferences = items.Where(i => output.Exported.Contains(i.Id) && i.Kind == ItemKind.FileReference)
                .Select(i => i.ReferenceId).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            clipboardSequence = GetClipboardSequenceNumber();
        }
        return output.Exported;
    }
}
