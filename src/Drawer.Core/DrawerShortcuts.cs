using System.Text.Json.Serialization;

namespace Drawer.Core;

[Flags]
public enum ShortcutModifiers { Alt = 1, Control = 2, Shift = 4 }

public readonly record struct ShortcutBinding(ShortcutModifiers Modifiers, int VirtualKey)
{
    [JsonIgnore] public bool IsValid =>
        (Modifiers & ~(ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift)) == 0 &&
        (Modifiers & (ShortcutModifiers.Control | ShortcutModifiers.Alt)) != 0 &&
        (VirtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x7A ||
            VirtualKey == 0x20 && Modifiers.HasFlag(ShortcutModifiers.Control) && Modifiers.HasFlag(ShortcutModifiers.Alt)) &&
        !(Modifiers.HasFlag(ShortcutModifiers.Control) && !Modifiers.HasFlag(ShortcutModifiers.Alt) &&
            VirtualKey is 0x41 or 0x43 or 0x58 or 0x56 or 0x5A or 0x59) &&
        !(Modifiers.HasFlag(ShortcutModifiers.Alt) && VirtualKey == 0x73);

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ShortcutModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ShortcutModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ShortcutModifiers.Shift)) parts.Add("Shift");
        parts.Add(VirtualKey switch { >= 0x70 and <= 0x7A => "F" + (VirtualKey - 0x6F), 0x20 => "Space", _ => ((char)VirtualKey).ToString() });
        return string.Join("+", parts);
    }
}

public interface IShortcutRegistration
{
    bool Register(int id, ShortcutBinding binding);
    void Unregister(int id);
}

public sealed class DrawerShortcuts(IShortcutRegistration registration) : IDisposable
{
    private sealed record Entry(int Id, ShortcutBinding Binding);
    private readonly Dictionary<Guid, Entry> active = [];
    private int nextId = 1;
    public ShortcutBinding? GetActive(Guid drawerId) => active.TryGetValue(drawerId, out var entry) ? entry.Binding : null;
    public Guid? Resolve(int registrationId) => active.FirstOrDefault(p => p.Value.Id == registrationId) is var pair && pair.Value is not null ? pair.Key : null;

    public bool TryApply(Guid drawerId, ShortcutBinding? binding, Action persist, out string? error)
    {
        error = null;
        if (binding is { IsValid: false }) { error = "请使用 Ctrl 或 Alt 搭配字母、数字或 F1–F11，避开基础操作快捷键。"; return false; }
        if (binding is not null && active.Any(p => p.Key != drawerId && p.Value.Binding == binding))
        { error = "此快捷键已绑定到其他抽屉。"; return false; }
        active.TryGetValue(drawerId, out var previous);
        bool changed = previous?.Binding != binding;
        int candidateId = 0;
        if (changed && binding is { } candidate)
        {
            // Never immediately reuse an ID: already queued messages must not open a different drawer.
            if (nextId > 0xBFFF) { error = "本次会话修改次数过多，请重启后重试。"; return false; }
            candidateId = nextId++;
            if (!registration.Register(candidateId, candidate))
            { error = "此组合键已被其他应用占用，或系统不允许注册，请换一个。"; return false; }
        }
        try { persist(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            if (candidateId != 0) registration.Unregister(candidateId);
            error = ex is InvalidDataException ? ex.Message : "保存失败，原快捷键保持不变，请重试。";
            return false;
        }
        if (changed)
        {
            Remove(drawerId);
            if (binding is { } committed) active[drawerId] = new(candidateId, committed);
        }
        return true;
    }
    public void Remove(Guid drawerId)
    {
        if (active.Remove(drawerId, out var entry)) registration.Unregister(entry.Id);
    }
    public void Dispose()
    {
        foreach (var id in active.Keys.ToArray()) Remove(id);
    }
}
