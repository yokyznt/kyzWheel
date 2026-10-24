using System.Runtime.InteropServices;

namespace KyzWheel.Platform.Native;

public static partial class CursorLock
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClipCursor(in RECT rect);

    [LibraryImport("user32.dll", EntryPoint = "ClipCursor")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClipCursorNull(nint rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    public static bool Active { get; private set; }

    public static void Lock(int left, int top, int right, int bottom)
    {
        ClipCursor(new RECT { Left = left, Top = top, Right = right, Bottom = bottom });
        Active = true;
    }

    public static void MoveTo(int x, int y) => SetCursorPos(x, y);

    public static void Release()
    {
        ClipCursorNull(0);
        Active = false;
    }
}
