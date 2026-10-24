using System.Runtime.InteropServices;
using KyzWheel.Platform.Native;

namespace KyzWheel.Platform.Shell;

public static partial class ShellSelection
{
    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int GetClassName(nint hwnd, char* buf, int max);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint parent, nint after, string? cls, string? title);

    private static unsafe string ClassOf(nint hwnd)
    {
        var buf = stackalloc char[256];
        var n = GetClassName(hwnd, buf, 256);
        return new string(buf, 0, n);
    }

    public static nint Foreground() => User32.GetAncestor(GetForegroundWindow(), 2);

    public static bool IsShellWindow(nint hwnd)
    {
        if (hwnd == 0) return false;
        var c = ClassOf(hwnd);
        return c is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW";
    }

    public static IReadOnlyList<string> Selected(nint hwnd)
    {
        if (!IsShellWindow(hwnd)) return [];
        try
        {
            var cls = ClassOf(hwnd);
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return [];
            dynamic shell = Activator.CreateInstance(type)!;
            try
            {
                if (cls is "Progman" or "WorkerW") return Desktop(shell);
                var activeTab = FindWindowEx(hwnd, 0, "ShellTabWindowClass", null);
                var candidates = new List<IReadOnlyList<string>>();
                foreach (dynamic w in shell.Windows())
                {
                    try
                    {
                        if ((nint)(long)w.HWND != hwnd) continue;
                        var items = Items(w.Document);
                        if (activeTab != 0 && IsActiveTab(w, activeTab)) return items;
                        candidates.Add(items);
                    }
                    catch (COMException)
                    {
                    }
                }
                return candidates.FirstOrDefault(c => c.Count > 0) ?? [];
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static List<string> Items(dynamic doc)
    {
        var list = new List<string>();
        foreach (dynamic item in doc.SelectedItems())
        {
            string? p = item.Path;
            if (!string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p))) list.Add(p);
        }
        return list;
    }

    private static IReadOnlyList<string> Desktop(dynamic shell)
    {
        object loc = 0;
        object root = null!;
        dynamic windows = shell.Windows();
        dynamic? desk = windows.FindWindowSW(ref loc, ref root, 8, out int _, 1);
        if (desk is null) return [];
        return Items(desk.Document);
    }

    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");

    private static bool IsActiveTab(dynamic w, nint activeTab)
    {
        try
        {
            var unk = Marshal.GetIUnknownForObject((object)w);
            try
            {
                var spIid = new Guid("6D5140C1-7436-11CE-8034-00AA006009FA");
                if (Marshal.QueryInterface(unk, in spIid, out var sp) != 0) return false;
                try
                {
                    var vtbl = Marshal.ReadIntPtr(sp);
                    var qs = Marshal.GetDelegateForFunctionPointer<QueryServiceFn>(Marshal.ReadIntPtr(vtbl, 3 * IntPtr.Size));
                    var sid = SID_STopLevelBrowser;
                    var iid = IID_IShellBrowser;
                    if (qs(sp, ref sid, ref iid, out var browser) != 0 || browser == 0) return false;
                    try
                    {
                        var bv = Marshal.ReadIntPtr(browser);
                        var getWindow = Marshal.GetDelegateForFunctionPointer<GetWindowFn>(Marshal.ReadIntPtr(bv, 3 * IntPtr.Size));
                        return getWindow(browser, out var tab) == 0 && tab == activeTab;
                    }
                    finally
                    {
                        Marshal.Release(browser);
                    }
                }
                finally
                {
                    Marshal.Release(sp);
                }
            }
            finally
            {
                Marshal.Release(unk);
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryServiceFn(nint self, ref Guid sid, ref Guid iid, out nint obj);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetWindowFn(nint self, out nint hwnd);
}
