using System.Diagnostics;
using System.Runtime.InteropServices;
using KyzWheel.Core.Lifecycle;

namespace KyzWheel.Guard;

public static partial class Program
{
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint hwnd, string text, string caption, uint type);

    public static int Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\kyzWheel.guard", out var first);
        if (!first) return 0;
        var app = Path.Combine(AppContext.BaseDirectory, "KyzWheel.App.exe");
        if (!File.Exists(app)) return 2;
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "kyzWheel");
        Directory.CreateDirectory(logDir);
        var log = Path.Combine(logDir, "guard.log");
        var policy = new RestartPolicy(TimeProvider.System);
        while (true)
        {
            int code;
            try
            {
                using var p = Process.Start(new ProcessStartInfo(app, "--guarded") { UseShellExecute = false })!;
                p.WaitForExit();
                code = p.ExitCode;
            }
            catch (Exception e)
            {
                File.AppendAllText(log, $"{DateTimeOffset.Now:O} start failed {e.Message}{Environment.NewLine}");
                code = -1;
            }
            if (code != 0) File.AppendAllText(log, $"{DateTimeOffset.Now:O} exit {code}{Environment.NewLine}");
            var delay = policy.OnExit(code);
            if (delay is null)
            {
                if (code != 0)
                    MessageBoxW(0, $"kyzWheel se cerró varias veces seguidas y no se volverá a abrir solo.\n\nRegistro: {log}", "kyzWheel", 0x30);
                return 0;
            }
            Thread.Sleep(delay.Value);
        }
    }
}
