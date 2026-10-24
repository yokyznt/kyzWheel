using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using KyzWheel.Platform.Native;
using static KyzWheel.Platform.Native.User32;

namespace KyzWheel.Platform.Input;

public enum MouseKind { LeftDown, LeftUp, Move, X1Down, X2Down, MiddleDown, Wheel }

public readonly record struct MouseHookEvent(MouseKind Kind, int X, int Y, int WheelDelta);

public readonly record struct KeyHookEvent(int VirtualKey, bool Down, bool Injected);

public sealed class InputThread : IDisposable
{
    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly ManualResetEventSlim _ready = new();
    private readonly HookProc _mouseProc;
    private readonly HookProc _keyProc;
    private uint _threadId;
    private nint _mouseHook;
    private nint _keyHook;

    public event Func<MouseHookEvent, bool>? Mouse;
    public event Func<KeyHookEvent, bool>? Key;
    public event Action<int>? Hotkey;

    public InputThread()
    {
        _mouseProc = MouseProc;
        _keyProc = KeyProc;
        _thread = new Thread(Run) { IsBackground = true, Name = "kyz-input" };
        _thread.Start();
        _ready.Wait();
    }

    public T Invoke<T>(Func<T> fn)
    {
        if (GetCurrentThreadId() == _threadId) return fn();
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue(() =>
        {
            try { tcs.SetResult(fn()); }
            catch (Exception e) { tcs.SetException(e); }
        });
        PostThreadMessageW(_threadId, WM_APP, 0, 0);
        return tcs.Task.GetAwaiter().GetResult();
    }

    public void Rehook() => Invoke(() =>
    {
        if (_mouseHook != 0) UnhookWindowsHookEx(_mouseHook);
        if (_keyHook != 0) UnhookWindowsHookEx(_keyHook);
        _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc, 0, 0);
        _keyHook = SetWindowsHookExW(WH_KEYBOARD_LL, _keyProc, 0, 0);
        return true;
    });

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        PeekMessageW(out _, 0, 0x400, 0x400, 0);
        _ready.Set();
        _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc, 0, 0);
        _keyHook = SetWindowsHookExW(WH_KEYBOARD_LL, _keyProc, 0, 0);
        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            if (msg.message == WM_APP)
                while (_work.TryDequeue(out var a)) a();
            else if (msg.message == WM_HOTKEY)
                Hotkey?.Invoke((int)msg.wParam);
        }
        if (_mouseHook != 0) UnhookWindowsHookEx(_mouseHook);
        if (_keyHook != 0) UnhookWindowsHookEx(_keyHook);
    }

    private nint MouseProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && Mouse is { } handler)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            MouseKind? kind = (uint)wParam switch
            {
                WM_LBUTTONDOWN => MouseKind.LeftDown,
                WM_LBUTTONUP => MouseKind.LeftUp,
                WM_MOUSEMOVE => MouseKind.Move,
                WM_MBUTTONDOWN => MouseKind.MiddleDown,
                WM_MOUSEWHEEL => MouseKind.Wheel,
                WM_XBUTTONDOWN => (info.mouseData >> 16) == 1 ? MouseKind.X1Down : MouseKind.X2Down,
                _ => null,
            };
            if (kind is { } k && handler(new MouseHookEvent(k, info.pt.X, info.pt.Y, (short)(info.mouseData >> 16))))
                return 1;
        }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private nint KeyProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && Key is { } handler)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var msg = (uint)wParam;
            var down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            if ((down || msg is WM_KEYUP or WM_SYSKEYUP) && handler(new KeyHookEvent((int)info.vkCode, down, (info.flags & 0x10) != 0)))
                return 1;
        }
        return CallNextHookEx(_keyHook, code, wParam, lParam);
    }

    public void Dispose()
    {
        PostThreadMessageW(_threadId, WM_QUIT, 0, 0);
        _thread.Join(1000);
    }
}
