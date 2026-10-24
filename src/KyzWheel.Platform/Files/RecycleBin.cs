using System.Runtime.InteropServices;

namespace KyzWheel.Platform.Files;

public static partial class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public nint hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public nint hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);

    public static bool Send(params IEnumerable<string> paths)
    {
        var list = paths.Where(File.Exists).ToList();
        if (list.Count == 0) return false;
        var op = new SHFILEOPSTRUCT
        {
            wFunc = 3,
            pFrom = string.Join('\0', list) + "\0\0",
            fFlags = 0x0040 | 0x0010 | 0x0004 | 0x0400,
        };
        return SHFileOperationW(ref op) == 0;
    }
}
