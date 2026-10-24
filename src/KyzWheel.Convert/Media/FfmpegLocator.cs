using System.IO.Compression;
using System.Security.Cryptography;

namespace KyzWheel.Convert.Media;

public static class FfmpegLocator
{
    public const string ZipUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static string LocalDir { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "kyzWheel.Data", "ffmpeg");

    public static string? Find(string exe = "ffmpeg.exe")
    {
        var local = Path.Combine(LocalDir, exe);
        if (File.Exists(local)) return local;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException)
            {
            }
        }
        return null;
    }

    public static async Task<string> EnsureAsync(IProgress<double>? progress, CancellationToken ct)
    {
        if (Find() is { } found) return found;
        await Gate.WaitAsync(ct);
        try
        {
            if (Find() is { } again) return again;
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            var expected = (await http.GetStringAsync(ZipUrl + ".sha256", ct)).Trim().Split(' ')[0].ToLowerInvariant();
            Directory.CreateDirectory(LocalDir);
            var zip = Path.Combine(LocalDir, "ffmpeg.zip");
            using (var resp = await http.GetAsync(ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? 0;
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(zip);
                var buf = new byte[1 << 16];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    read += n;
                    if (total > 0) progress?.Report(0.9 * read / total);
                }
            }
            string actual;
            await using (var fs = File.OpenRead(zip)) actual = System.Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, ct));
            if (actual != expected)
            {
                File.Delete(zip);
                throw new ConversionException("La descarga de ffmpeg no pasó la verificación");
            }
            using (var archive = ZipFile.OpenRead(zip))
                foreach (var e in archive.Entries.Where(e => e.FullName.EndsWith("/bin/ffmpeg.exe") || e.FullName.EndsWith("/bin/ffprobe.exe")))
                    e.ExtractToFile(Path.Combine(LocalDir, e.Name), true);
            File.Delete(zip);
            progress?.Report(1);
            return Find() ?? throw new ConversionException("No se pudo preparar ffmpeg");
        }
        catch (HttpRequestException e)
        {
            throw new ConversionException("Se necesita internet una vez para descargar ffmpeg (video y audio)", e);
        }
        finally
        {
            Gate.Release();
        }
    }
}
