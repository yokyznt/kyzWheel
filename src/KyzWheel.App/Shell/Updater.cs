using Velopack;
using Velopack.Sources;

namespace KyzWheel.App.Shell;

public static class Updater
{
    private static UpdateManager? _mgr;
    private static UpdateInfo? _ready;

    public static string? ReadyVersion => _ready?.TargetFullRelease.Version.ToString();

    public static async Task<string?> CheckAsync()
    {
        try
        {
            _mgr ??= new UpdateManager(new GithubSource("https://github.com/yokyznt/kyzWheel", null, false));
            if (!_mgr.IsInstalled) return null;
            var update = await _mgr.CheckForUpdatesAsync();
            if (update is null) return null;
            await _mgr.DownloadUpdatesAsync(update);
            _ready = update;
            _mgr.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);
            return ReadyVersion;
        }
        catch (Exception e)
        {
            Log.Error(e);
            return null;
        }
    }

    public static void InstallNow()
    {
        if (_mgr is null || _ready is null) return;
        foreach (var name in new[] { "KyzWheel.Guard", "wa-sidecar" })
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                try { p.Kill(true); } catch (Exception) { }
        _mgr.ApplyUpdatesAndRestart(_ready.TargetFullRelease);
    }
}
