using System.Runtime.InteropServices;

namespace KyzWheel.Platform.Native;

public static partial class Foreground
{
    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint from, uint to, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial void keybd_event(byte vk, byte scan, uint flags, nint extra);

    public static bool Force(nint hwnd)
    {
        var fg = GetForegroundWindow();
        if (fg == hwnd) return true;
        var fgThread = User32.GetWindowThreadProcessId(fg, out _);
        var me = User32.GetCurrentThreadId();
        var attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
        try
        {
            if (!SetForegroundWindow(hwnd))
            {
                keybd_event(0x12, 0, 0, 0);
                keybd_event(0x12, 0, 2, 0);
                SetForegroundWindow(hwnd);
            }
            BringWindowToTop(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(me, fgThread, false);
        }
        return GetForegroundWindow() == hwnd;
    }
}
