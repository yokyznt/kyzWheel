using Velopack;
using Velopack.Sources;

namespace KyzWheel.App.Shell;

public static class Updater
{
    public static async Task CheckAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource("https://github.com/yokyznt/kyzWheel", null, false));
            if (!mgr.IsInstalled) return;
            var update = await mgr.CheckForUpdatesAsync();
            if (update is null) return;
            await mgr.DownloadUpdatesAsync(update);
            mgr.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }
}
