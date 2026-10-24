using ImageMagick;

namespace KyzWheel.Convert.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("kyzc").FullName;

    public string Png(string name = "foto.png", bool withGps = true)
    {
        var p = System.IO.Path.Combine(Path, name);
        using var img = new MagickImage(MagickColors.Red, 64, 64);
        img.Draw(new ImageMagick.Drawing.Drawables().FillColor(MagickColors.Blue).Rectangle(10, 10, 40, 40));
        if (withGps)
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.GPSLatitudeRef, "N");
            exif.SetValue(ExifTag.Make, "kyzCam");
            img.SetProfile(exif);
        }
        img.Write(p, MagickFormat.Png);
        return p;
    }

    public string Jpeg(string name = "foto.jpg", int size = 512)
    {
        var p = System.IO.Path.Combine(Path, name);
        using var img = new MagickImage("plasma:", (uint)size, (uint)size);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.Make, "kyzCam");
        img.SetProfile(exif);
        img.Quality = 98;
        img.Write(p, MagickFormat.Jpeg);
        return p;
    }

    public string Convert(string source, MagickFormat format, string ext)
    {
        var p = System.IO.Path.ChangeExtension(source, ext);
        using var img = new MagickImage(source);
        if (format == MagickFormat.Heic) KyzWheel.Convert.Images.HeicEncoder.WriteAsync(img, p).GetAwaiter().GetResult();
        else img.Write(p, format);
        return p;
    }

    public void Dispose() => Directory.Delete(Path, true);
}
