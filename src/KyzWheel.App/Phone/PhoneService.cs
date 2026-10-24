using System.Diagnostics;
using KyzWheel.Core.Phone;

namespace KyzWheel.App.Phone;

public sealed class PhoneException(string message) : Exception(message);

public sealed class PhoneService
{
    public static string? CliPath()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "KDE Connect", "bin", "kdeconnect-cli.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "KDE Connect", "bin", "kdeconnect-cli.exe"),
        ];
        foreach (var c in candidates) if (File.Exists(c)) return c;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim('"'), "kdeconnect-cli.exe");
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException)
            {
            }
        }
        return null;
    }

    public static string? AppPath()
    {
        var cli = CliPath();
        if (cli is null) return null;
        var app = Path.Combine(Path.GetDirectoryName(cli)!, "kdeconnect-app.exe");
        return File.Exists(app) ? app : null;
    }

    public bool Installed => CliPath() is not null;

    public PhoneDevice? Last { get; private set; }

    private static async Task<string> RunAsync(IEnumerable<string> args, int timeoutMs = 15000)
    {
        var cli = CliPath() ?? throw new PhoneException("Falta KDE Connect en la PC");
        var psi = new ProcessStartInfo(cli) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new PhoneException("No se pudo abrir KDE Connect");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch (InvalidOperationException) { }
            throw new PhoneException("El celular no respondió; revisa que esté en la misma red Wi-Fi");
        }
        var err = (await stderr).Trim();
        if (p.ExitCode != 0) throw new PhoneException(err.Length > 0 ? err : "KDE Connect falló");
        return (await stdout) + "\n" + err;
    }

    public async Task<IReadOnlyList<PhoneDevice>> DevicesAsync()
    {
        var list = PhoneCli.ParseDevices(await RunAsync(PhoneCli.ListAvailable));
        Last = list.FirstOrDefault();
        return list;
    }

    private async Task<PhoneDevice> DeviceAsync()
    {
        var list = await DevicesAsync();
        return list.FirstOrDefault() ?? throw new PhoneException("No hay celular vinculado o no está en la misma red");
    }

    public async Task<string> SendFilesAsync(IReadOnlyList<string> files)
    {
        var d = await DeviceAsync();
        await RunAsync(PhoneCli.Share(d.Id, files), 60000);
        return d.Name;
    }

    public async Task<string> SendClipboardAsync()
    {
        var d = await DeviceAsync();
        await RunAsync(PhoneCli.SendClipboard(d.Id));
        return d.Name;
    }

    public async Task<string> SendTextAsync(string text)
    {
        var d = await DeviceAsync();
        await RunAsync(PhoneCli.ShareText(d.Id, text));
        return d.Name;
    }

    public async Task<string> RingAsync()
    {
        var d = await DeviceAsync();
        await RunAsync(PhoneCli.Ring(d.Id));
        return d.Name;
    }

    public async Task<string> PingAsync(string msg)
    {
        var d = await DeviceAsync();
        await RunAsync(PhoneCli.Ping(d.Id, msg));
        return d.Name;
    }
}
