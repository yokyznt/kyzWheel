using System.Runtime.InteropServices;

namespace KyzWheel.Platform.Native;

public static partial class Keys
{
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
        public long pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint n, [In] INPUT[] inputs, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    private static INPUT K(ushort vk, bool up) => new() { type = 1, ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0u } };

    public static void PasteInto(nint hwnd)
    {
        if (hwnd != 0) SetForegroundWindow(hwnd);
        Thread.Sleep(60);
        SendInput(4, [K(0x11, false), K(0x56, false), K(0x56, true), K(0x11, true)], Marshal.SizeOf<INPUT>());
    }
}
