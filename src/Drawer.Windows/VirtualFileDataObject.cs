using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Drawer.Windows;

public sealed record VirtualFile(string Name, string Path);

// FILECONTENTS is streamed only when the receiver requests that particular file.
// This adapter is used for image-only file sets; mixed sets retain CF_HDROP compatibility.
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class VirtualFileDataObject : ComDataObject
{
    private readonly ComDataObject fallback;
    private readonly IReadOnlyList<VirtualFile> files;
    private readonly short descriptor = unchecked((short)DataFormats.GetDataFormat("FileGroupDescriptorW").Id);
    private readonly short contents = unchecked((short)DataFormats.GetDataFormat("FileContents").Id);
    private const int FormatError = unchecked((int)0x80040064);
    public VirtualFileDataObject(DataObject fallback, IReadOnlyList<VirtualFile> files)
    { this.fallback = (ComDataObject)fallback; this.files = files; }

    private FORMATETC Format(short id, TYMED tymed, int index = -1) => new() { cfFormat = id, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = index, tymed = tymed };
    public void GetData(ref FORMATETC format, out STGMEDIUM medium)
    {
        medium = default;
        if (format.cfFormat == descriptor)
        {
            Marshal.ThrowExceptionForHR(QueryGetData(ref format));
            byte[] bytes = DescriptorBytes();
            nint memory = GlobalAlloc(2, (nuint)bytes.Length);
            if (memory == 0) throw new OutOfMemoryException();
            nint pointer = GlobalLock(memory);
            if (pointer == 0) { GlobalFree(memory); throw new OutOfMemoryException(); }
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { GlobalUnlock(memory); }
            medium = new() { tymed = TYMED.TYMED_HGLOBAL, unionmember = memory };
            return;
        }
        if (format.cfFormat == contents)
        {
            Marshal.ThrowExceptionForHR(QueryGetData(ref format));
            int hr = SHCreateStreamOnFileEx(files[format.lindex].Path, 0x40, 0, false, 0, out nint stream);
            Marshal.ThrowExceptionForHR(hr);
            medium = new() { tymed = TYMED.TYMED_ISTREAM, unionmember = stream };
            return;
        }
        fallback.GetData(ref format, out medium);
    }
    private byte[] DescriptorBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode);
        writer.Write(files.Count);
        foreach (var file in files)
        {
            long size = new FileInfo(file.Path).Length;
            writer.Write(0x80000044u); // FD_UNICODE | FD_FILESIZE | FD_ATTRIBUTES
            writer.Write(new byte[32]); // CLSID, SIZEL, POINTL
            writer.Write(0x80u); // FILE_ATTRIBUTE_NORMAL
            writer.Write(new byte[24]); // timestamps are unspecified
            writer.Write((uint)(size >> 32)); writer.Write((uint)size);
            var name = new byte[520];
            Encoding.Unicode.GetBytes(file.Name[..Math.Min(file.Name.Length, 259)]).CopyTo(name, 0);
            writer.Write(name);
        }
        return stream.ToArray();
    }
    public int QueryGetData(ref FORMATETC format)
    {
        if (format.cfFormat == descriptor)
            return format.dwAspect == DVASPECT.DVASPECT_CONTENT && (format.tymed & TYMED.TYMED_HGLOBAL) != 0 ? 0 : FormatError;
        if (format.cfFormat == contents)
            return format.dwAspect == DVASPECT.DVASPECT_CONTENT && (format.tymed & TYMED.TYMED_ISTREAM) != 0 && format.lindex >= 0 && format.lindex < files.Count ? 0 : FormatError;
        return fallback.QueryGetData(ref format);
    }
    public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
    {
        if (direction != DATADIR.DATADIR_GET) return fallback.EnumFormatEtc(direction);
        var formats = new List<FORMATETC> { Format(descriptor, TYMED.TYMED_HGLOBAL), Format(contents, TYMED.TYMED_ISTREAM) };
        var enumerator = fallback.EnumFormatEtc(direction);
        var one = new FORMATETC[1]; var fetched = new int[1];
        while (enumerator.Next(1, one, fetched) == 0) formats.Add(one[0]);
        return new FormatEnumerator(formats.ToArray());
    }
    public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => fallback.GetDataHere(ref format, ref medium);
    public int GetCanonicalFormatEtc(ref FORMATETC input, out FORMATETC output) { output = input; output.ptd = 0; return 0x40130; }
    public void SetData(ref FORMATETC format, ref STGMEDIUM medium, bool release) => fallback.SetData(ref format, ref medium, release);
    public int DAdvise(ref FORMATETC format, ADVF flags, IAdviseSink sink, out int connection) => fallback.DAdvise(ref format, flags, sink, out connection);
    public void DUnadvise(int connection) => fallback.DUnadvise(connection);
    public int EnumDAdvise(out IEnumSTATDATA? result) => fallback.EnumDAdvise(out result);

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class FormatEnumerator(FORMATETC[] formats) : IEnumFORMATETC
    {
        private int position;
        public int Next(int count, FORMATETC[] result, int[]? fetched)
        {
            int n = Math.Min(count, formats.Length - position);
            Array.Copy(formats, position, result, 0, n); position += n;
            if (fetched is { Length: > 0 }) fetched[0] = n;
            return n == count ? 0 : 1;
        }
        public int Skip(int count) { int old = position; position = Math.Min(formats.Length, position + count); return position - old == count ? 0 : 1; }
        public int Reset() { position = 0; return 0; }
        public void Clone(out IEnumFORMATETC clone) => clone = new FormatEnumerator(formats) { position = position };
    }
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateStreamOnFileEx(string path, uint mode, uint attributes, [MarshalAs(UnmanagedType.Bool)] bool create, nint template, out nint stream);
}
