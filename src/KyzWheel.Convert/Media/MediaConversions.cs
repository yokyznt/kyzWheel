using System.Diagnostics;
using System.Text;
using KyzWheel.Core.Files;

namespace KyzWheel.Convert.Media;

public static class MediaConversions
{
    internal static readonly HashSet<string> Video = ["mp4", "mov", "mkv", "webm", "avi", "m4v", "wmv", "flv", "gif"];
    internal static readonly HashSet<string> Audio = ["mp3", "wav", "m4a", "aac", "flac", "ogg", "opus", "wma"];

    public static IReadOnlyList<IConversion> All() =>
    [
        new FfmpegConversion("media.mp4", "MP4", "\uE714", "mp4", Video, ["mp4"], "-c:v libx264 -preset medium -crf 23 -pix_fmt yuv420p -c:a aac -b:a 160k -movflags +faststart"),
        new FfmpegConversion("media.gif", "GIF", "\uE8B9", "gif", Video, ["gif"], "-vf \"fps=12,scale='min(480,iw)':-2:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse\" -an"),
        new FfmpegConversion("media.mp3", "MP3", "\uE8D6", "mp3", [.. Video.Except(["gif"]), .. Audio], ["mp3"], "-vn -c:a libmp3lame -q:a 2"),
        new FfmpegConversion("media.wav", "WAV", "\uE8D6", "wav", [.. Video.Except(["gif"]), .. Audio], ["wav"], "-vn -c:a pcm_s16le"),
        new FfmpegConversion("media.mov", "MOV", "\uE714", "mov", Video, ["mov"], "-c:v libx264 -crf 20 -pix_fmt yuv420p -c:a aac -b:a 192k"),
        new FfmpegConversion("media.webm", "WEBM", "\uE714", "webm", Video, ["webm"], "-c:v libvpx-vp9 -crf 33 -b:v 0 -row-mt 1 -c:a libopus -b:a 128k"),
        new FfmpegConversion("media.m4a", "M4A", "\uE8D6", "m4a", [.. Video.Except(["gif"]), .. Audio], ["m4a"], "-vn -c:a aac -b:a 192k"),
        new FfmpegConversion("media.compress", "Comprimir", "\uE73F", null, [.. Video.Except(["gif"])], [], "-c:v libx264 -crf 28 -preset medium -pix_fmt yuv420p -c:a aac -b:a 128k -movflags +faststart"),
        new FfmpegConversion("media.mute", "Silenciar", "\uE74F", null, [.. Video.Except(["gif"])], [], "-c copy -an"),
    ];

    public static string Arguments(string id) => ((FfmpegConversion)All().Single(c => c.Id == id)).Args;
}

internal sealed class FfmpegConversion(string id, string label, string glyph, string? ext, IEnumerable<string> inputs, string[] same, string args) : IConversion
{
    private readonly HashSet<string> _inputs = [.. inputs];

    public string Id => id;
    public string Label => label;
    public string Glyph => glyph;
    public string Args => args;
    public ConverterPage Page => ConverterPage.Media;

    public bool CanHandle(string path)
    {
        var e = FileGuards.Ext(path);
        return _inputs.Contains(e) && !same.Contains(e);
    }

    public async Task<ConversionResult> RunAsync(string path, IProgress<double>? progress, CancellationToken ct)
    {
        FileGuards.EnsureLocal(path);
        var ffmpeg = await FfmpegLocator.EnsureAsync(null, ct);
        var outExt = ext is null ? Path.GetExtension(path) : "." + ext;
        var output = OutputNaming.NextFreePath(path, outExt);
        var tmp = output + ".kyztmp" + outExt;
        var psi = new ProcessStartInfo(ffmpeg, $"-hide_banner -nostdin -y -i \"{path}\" {args} \"{tmp}\"")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = new Process { StartInfo = psi };
        var duration = TimeSpan.Zero;
        var tail = new Queue<string>();
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            if (duration == TimeSpan.Zero && FfmpegProgress.ParseDuration(e.Data) is { } d) duration = d;
            if (FfmpegProgress.Parse(e.Data, duration) is { } v) progress?.Report(v);
            tail.Enqueue(e.Data);
            if (tail.Count > 8) tail.Dequeue();
        };
        p.Start();
        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception) { }
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch (Exception) { }
            File.Delete(tmp);
            throw;
        }
        if (p.ExitCode != 0 || !File.Exists(tmp))
        {
            File.Delete(tmp);
            throw new ConversionException("No se pudo convertir: " + (tail.LastOrDefault(l => l.Contains("rror") || l.Contains("nvalid")) ?? "ffmpeg falló"));
        }
        File.Move(tmp, output);
        progress?.Report(1);
        return new ConversionResult(path, output, new FileInfo(path).Length, new FileInfo(output).Length);
    }
}
