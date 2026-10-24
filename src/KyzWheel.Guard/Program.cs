using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using KyzWheel.Core.Lifecycle;
using Microsoft.Win32;
using Velopack;

namespace KyzWheel.Guard;

public static partial class Program
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint hwnd, string text, string caption, uint type);

    public static int Main(string[] args)
    {
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => EnableAutostart())
            .OnAfterUpdateFastCallback(_ => EnableAutostart())
            .OnBeforeUninstallFastCallback(_ => DisableAutostart())
            .Run();
        var autostart = args.Contains("--autostart");
        using var mutex = new Mutex(true, @"Local\kyzWheel.guard", out var first);
        if (!first)
        {
            if (!autostart) Signal("settings");
            return 0;
        }
        var app = Path.Combine(AppContext.BaseDirectory, "KyzWheel.App.exe");
        if (!File.Exists(app)) return 2;
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "kyzWheel.Data");
        Directory.CreateDirectory(logDir);
        var log = Path.Combine(logDir, "guard.log");
        var policy = new RestartPolicy(TimeProvider.System);
        var openSettings = !autostart;
        while (true)
        {
            int code;
            try
            {
                var startArgs = openSettings ? "--guarded --settings" : "--guarded";
                openSettings = false;
                using var p = Process.Start(new ProcessStartInfo(app, startArgs) { UseShellExecute = false })!;
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

    private static void Signal(string command)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", "kyzWheel.ipc", PipeDirection.Out);
            c.Connect(1500);
            using var w = new StreamWriter(c);
            w.WriteLine(command);
        }
        catch (Exception)
        {
        }
    }

    private static void EnableAutostart()
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        k.SetValue("kyzWheel", $"\"{Environment.ProcessPath}\" --autostart");
    }

    private static void DisableAutostart()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
        k?.DeleteValue("kyzWheel", false);
    }
}
