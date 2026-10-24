using KyzWheel.Core.Input;
using static KyzWheel.Platform.Native.User32;

namespace KyzWheel.Platform.Input;

public sealed class GlobalHotkeyService : IDisposable
{
    private readonly InputThread _input;
    private readonly Dictionary<int, string> _ids = new();
    private readonly Dictionary<string, Hotkey> _mouse = new();
    private readonly ChordTracker _chords = new();
    private readonly Dictionary<string, long> _lastFire = new();
    private int _next = 1;

    public event Action<string>? Triggered;

    public GlobalHotkeyService(InputThread input)
    {
        _input = input;
        _input.Hotkey += n => { if (_ids.TryGetValue(n, out var id)) Fire(id); };
        _input.Mouse += OnMouse;
        _input.Key += OnKey;
    }

    private void Fire(string id)
    {
        var now = Environment.TickCount64;
        if (_lastFire.TryGetValue(id, out var last) && now - last < 350) return;
        _lastFire[id] = now;
        Triggered?.Invoke(id);
    }

    public bool Register(string id, Hotkey h) => _input.Invoke(() =>
    {
        if (h.Mouse is not null)
        {
            _mouse[id] = h;
            return true;
        }
        _chords.Set(id, h);
        if (h.ModifierOnly) return true;
        var n = _next++;
        uint mods = MOD_NOREPEAT;
        if (h.Mods.HasFlag(Mods.Alt)) mods |= MOD_ALT;
        if (h.Mods.HasFlag(Mods.Ctrl)) mods |= MOD_CONTROL;
        if (h.Mods.HasFlag(Mods.Shift)) mods |= MOD_SHIFT;
        if (h.Mods.HasFlag(Mods.Win)) mods |= MOD_WIN;
        if (RegisterHotKey(0, n, mods, (uint)h.VirtualKey)) _ids[n] = id;
        return true;
    });

    public void UnregisterAll() => _input.Invoke(() =>
    {
        foreach (var n in _ids.Keys) UnregisterHotKey(0, n);
        _ids.Clear();
        _mouse.Clear();
        _chords.Clear();
        return true;
    });

    private static bool Held(int vk) => vk == 0x5B ? IsDown(VK_LWIN) || IsDown(VK_RWIN) : IsDown(vk);

    private bool OnKey(KeyHookEvent e)
    {
        if (e.Injected || _chords.Count == 0) return false;
        if (!e.Down)
        {
            var u = _chords.Up(e.VirtualKey);
            if (u.Fired is { } uid) Fire(uid);
            return u.Swallow;
        }
        var me = ChordTracker.Normalize(e.VirtualKey);
        _chords.Prune(k => k == me || Held(k));
        var r = _chords.Down(e.VirtualKey);
        if (r.Fired is { } id) Fire(id);
        return r.Swallow;
    }

    private bool OnMouse(MouseHookEvent e)
    {
        MouseButton? b = e.Kind switch
        {
            MouseKind.X1Down => MouseButton.X1,
            MouseKind.X2Down => MouseButton.X2,
            MouseKind.MiddleDown => MouseButton.Middle,
            _ => null,
        };
        if (b is null || _mouse.Count == 0) return false;
        var mods = CurrentMods();
        foreach (var (id, h) in _mouse)
        {
            if (h.Mouse == b && h.Mods == mods)
            {
                Triggered?.Invoke(id);
                return true;
            }
        }
        return false;
    }

    public static Mods CurrentMods()
    {
        var m = Mods.None;
        if (IsDown(VK_CONTROL)) m |= Mods.Ctrl;
        if (IsDown(VK_MENU)) m |= Mods.Alt;
        if (IsDown(VK_SHIFT)) m |= Mods.Shift;
        if (IsDown(VK_LWIN) || IsDown(VK_RWIN)) m |= Mods.Win;
        return m;
    }

    public void Dispose() => UnregisterAll();
}
