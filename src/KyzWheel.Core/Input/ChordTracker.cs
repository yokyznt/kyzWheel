namespace KyzWheel.Core.Input;

public readonly record struct ChordResult(string? Fired, bool Swallow);

public sealed class ChordTracker
{
    private const int Ctrl = 0x11, Shift = 0x10, Alt = 0x12, Win = 0x5B;
    private readonly Dictionary<string, HashSet<int>> _keys = new();
    private readonly Dictionary<string, int> _main = new();
    private readonly HashSet<int> _down = new();
    private readonly HashSet<string> _fired = new();
    private readonly HashSet<int> _swallowUp = new();
    private readonly HashSet<string> _armed = new();

    public static int Normalize(int vk) => vk switch
    {
        0xA2 or 0xA3 => Ctrl,
        0xA0 or 0xA1 => Shift,
        0xA4 or 0xA5 => Alt,
        0x5C => Win,
        _ => vk,
    };

    public int Count => _keys.Count;

    public IReadOnlyCollection<int> Pressed => _down;

    public void Set(string id, Hotkey h)
    {
        var s = new HashSet<int>();
        if (!h.ModifierOnly) s.Add(Normalize(h.VirtualKey));
        if (h.Mods.HasFlag(Mods.Ctrl)) s.Add(Ctrl);
        if (h.Mods.HasFlag(Mods.Shift)) s.Add(Shift);
        if (h.Mods.HasFlag(Mods.Alt)) s.Add(Alt);
        if (h.Mods.HasFlag(Mods.Win)) s.Add(Win);
        _keys[id] = s;
        _main[id] = h.ModifierOnly ? 0 : Normalize(h.VirtualKey);
    }

    public void Clear()
    {
        _keys.Clear();
        _main.Clear();
        _fired.Clear();
        _armed.Clear();
    }

    public void Prune(Func<int, bool> isDown) => _down.RemoveWhere(k => !isDown(k));

    public ChordResult Down(int vk)
    {
        vk = Normalize(vk);
        _down.Add(vk);
        string? fired = null;
        var swallow = false;
        foreach (var (id, req) in _keys)
        {
            if (_main[id] == 0)
            {
                if (req.SetEquals(_down)) _armed.Add(id);
                else if (!req.Contains(vk)) _armed.Remove(id);
                continue;
            }
            if (!req.SetEquals(_down)) continue;
            if (vk == _main[id])
            {
                swallow = true;
                _swallowUp.Add(vk);
            }
            if (_fired.Add(id)) fired = id;
        }
        return new ChordResult(fired, swallow);
    }

    public ChordResult Up(int vk)
    {
        vk = Normalize(vk);
        _down.Remove(vk);
        string? fired = null;
        foreach (var (id, req) in _keys)
        {
            if (!req.Contains(vk)) continue;
            _fired.Remove(id);
            if (_armed.Remove(id)) fired ??= id;
        }
        return new ChordResult(fired, _swallowUp.Remove(vk));
    }
}
