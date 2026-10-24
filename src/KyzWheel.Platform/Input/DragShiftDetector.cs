using System.Runtime.InteropServices;
using KyzWheel.Core.Input;
using static KyzWheel.Platform.Native.User32;

namespace KyzWheel.Platform.Input;

public sealed partial class DragShiftDetector
{
    private readonly DragGestureMachine _machine = new(Math.Max(4, GetSystemMetrics(SM_CXDRAG)));
    private readonly Dictionary<uint, bool> _shellPids = new();
    private bool _shiftAtStart;
    private bool _shellSource;
    private bool _shiftDown;
    private int _x;
    private int _y;

    public event Action<GestureOutput, int, int>? Gesture;

    public bool Enabled { get; set; } = true;

    public DragShiftDetector(InputThread input)
    {
        input.Mouse += OnMouse;
        input.Key += OnKey;
    }

    private bool OnMouse(MouseHookEvent e)
    {
        if (!Enabled) return false;
        _x = e.X;
        _y = e.Y;
        switch (e.Kind)
        {
            case MouseKind.LeftDown:
                _shiftAtStart = IsDown(VK_SHIFT);
                _shellSource = IsShellAt(e.X, e.Y);
                Emit(GestureInput.LeftDown);
                return false;
            case MouseKind.Move:
                Emit(GestureInput.Move);
                return false;
            case MouseKind.LeftUp:
                Emit(GestureInput.LeftUp);
                return false;
            case MouseKind.Wheel when _machine.IsOpen:
                Emit(e.WheelDelta > 0 ? GestureInput.WheelUp : GestureInput.WheelDown);
                return true;
            default:
                return false;
        }
    }

    private bool OnKey(KeyHookEvent k)
    {
        if (!Enabled || k.Injected) return false;
        var isShift = k.VirtualKey is VK_SHIFT or VK_LSHIFT or VK_RSHIFT;
        if (isShift)
        {
            if (k.Down && !_shiftDown)
            {
                _shiftDown = true;
                Emit(IsDown(VK_MENU) ? GestureInput.AltShiftDown : GestureInput.ShiftDown);
            }
            else if (!k.Down)
            {
                _shiftDown = false;
                Emit(GestureInput.ShiftUp);
            }
            return false;
        }
        if (!k.Down || !_machine.IsOpen) return false;
        if (k.VirtualKey == VK_TAB)
        {
            Emit(GestureInput.TabDown);
            return true;
        }
        if (k.VirtualKey == VK_ESCAPE)
        {
            Emit(GestureInput.EscDown);
            return false;
        }
        return false;
    }

    private void Emit(GestureInput input)
    {
        var o = _machine.Feed(input, _x, _y, _shiftAtStart, _shellSource);
        if (o != GestureOutput.None) Gesture?.Invoke(o, _x, _y);
    }

    private bool IsShellAt(int x, int y)
    {
        var hwnd = GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), 2);
        if (hwnd == 0) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (_shellPids.TryGetValue(pid, out var cached)) return cached;
        var result = ProcessName(pid) is { } name && name.EndsWith("\\explorer.exe", StringComparison.OrdinalIgnoreCase);
        if (_shellPids.Count > 64) _shellPids.Clear();
        _shellPids[pid] = result;
        return result;
    }

    private static unsafe string? ProcessName(uint pid)
    {
        var h = OpenProcess(0x1000, false, pid);
        if (h == 0) return null;
        try
        {
            char* buf = stackalloc char[1024];
            int len = 1024;
            return QueryFullProcessImageNameW(h, 0, buf, ref len) ? new string(buf, 0, len) : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageNameW(nint h, uint flags, char* buffer, ref int size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint h);
}
