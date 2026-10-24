using Windows.Media.Control;

namespace KyzWheel.App.Tools;

public sealed class MediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? _mgr;

    public string? NowPlaying { get; private set; }
    public bool Playing { get; private set; }

    public async Task RefreshAsync()
    {
        try
        {
            _mgr ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var s = _mgr.GetCurrentSession();
            if (s is null)
            {
                NowPlaying = null;
                Playing = false;
                return;
            }
            var p = await s.TryGetMediaPropertiesAsync();
            NowPlaying = string.IsNullOrWhiteSpace(p?.Title) ? null : string.IsNullOrWhiteSpace(p.Artist) ? p.Title : $"{p.Title} — {p.Artist}";
            Playing = s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch (Exception)
        {
            NowPlaying = null;
        }
    }

    private async Task<bool> DoAsync(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> op)
    {
        try
        {
            _mgr ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var s = _mgr.GetCurrentSession();
            if (s is null) return false;
            var ok = await op(s);
            await Task.Delay(250);
            await RefreshAsync();
            return ok;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public Task<bool> ToggleAsync() => DoAsync(s => s.TryTogglePlayPauseAsync());
    public Task<bool> NextAsync() => DoAsync(s => s.TrySkipNextAsync());
    public Task<bool> PreviousAsync() => DoAsync(s => s.TrySkipPreviousAsync());
}
