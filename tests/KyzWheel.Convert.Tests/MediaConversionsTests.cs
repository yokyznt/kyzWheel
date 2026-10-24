using System.Diagnostics;
using KyzWheel.Convert.Media;

namespace KyzWheel.Convert.Tests;

public sealed class MediaConversionsTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    [Fact]
    public void Progress_parses_time_against_duration()
    {
        Assert.Equal(0.5, FfmpegProgress.Parse("frame=  48 fps=0.0 q=28.0 size=256kB time=00:00:02.00 bitrate=1048.6kbits/s", TimeSpan.FromSeconds(4)));
        Assert.Equal(TimeSpan.FromSeconds(83.5), FfmpegProgress.ParseDuration("  Duration: 00:01:23.50, start: 0.000000, bitrate: 1 kb/s"));
        Assert.Null(FfmpegProgress.Parse("Stream mapping:", TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public void Exact_arguments()
    {
        Assert.Equal("-vf \"fps=12,scale='min(480,iw)':-2:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse\" -an", MediaConversions.Arguments("media.gif"));
        Assert.Equal("-c:v libx264 -crf 28 -preset medium -pix_fmt yuv420p -c:a aac -b:a 128k -movflags +faststart", MediaConversions.Arguments("media.compress"));
        Assert.Equal("-c copy -an", MediaConversions.Arguments("media.mute"));
    }

    [Fact]
    public void Offers_right_actions()
    {
        var all = MediaConversions.All();
        Assert.Contains(all, c => c.Id == "media.mp3" && c.CanHandle("a.mp4"));
        Assert.DoesNotContain(all, c => c.Id == "media.mp4" && c.CanHandle("a.mp4"));
        Assert.DoesNotContain(all, c => c.Id == "media.gif" && c.CanHandle("a.mp3"));
        Assert.Contains(all, c => c.Id == "media.wav" && c.CanHandle("a.MP3"));
    }

    private string? Clip()
    {
        var ff = FfmpegLocator.Find();
        if (ff is null) return null;
        var p = Path.Combine(_t.Path, "clip.mp4");
        using var proc = Process.Start(new ProcessStartInfo(ff, $"-hide_banner -y -f lavfi -i testsrc=duration=2:size=320x240:rate=15 -f lavfi -i sine=frequency=440:duration=2 -shortest -c:v libx264 -pix_fmt yuv420p -c:a aac \"{p}\"") { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true })!;
        proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return File.Exists(p) ? p : null;
    }

    [Theory]
    [InlineData("media.mp3", ".mp3")]
    [InlineData("media.gif", ".gif")]
    [InlineData("media.mute", ".mp4")]
    [InlineData("media.webm", ".webm")]
    public async Task Converts_real_clip_when_ffmpeg_present(string id, string ext)
    {
        var clip = Clip();
        if (clip is null) return;
        var reports = new List<double>();
        var r = await MediaConversions.All().Single(c => c.Id == id).RunAsync(clip, new Progress<double>(reports.Add), CancellationToken.None);
        Assert.EndsWith(ext, r.OutputPath);
        Assert.True(r.SizeAfter > 0);
        Assert.NotEqual(clip, r.OutputPath);
    }
}
