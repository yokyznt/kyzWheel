using System.Runtime.InteropServices;

namespace KyzWheel.Platform.Native;

public static partial class Shcore
{
    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [LibraryImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyWorkingSet(nint process);

    public static double ScaleAt(int x, int y)
    {
        var mon = User32.MonitorFromPoint(new User32.POINT { X = x, Y = y }, 2);
        return GetDpiForMonitor(mon, 0, out var dx, out _) == 0 ? dx / 96.0 : 1.0;
    }

    public static void TrimWorkingSet() => EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle);
}
