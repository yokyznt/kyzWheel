using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using ImageMagick;

namespace KyzWheel.Convert.Images;

public static class HeicEncoder
{
    private static readonly Lazy<bool> Available = new(() =>
        BitmapEncoder.GetEncoderInformationEnumerator().Any(e => e.CodecId == BitmapEncoder.HeifEncoderId));

    public static bool IsAvailable => Available.Value;

    public static async Task WriteAsync(IMagickImage<byte> image, string path)
    {
        if (!IsAvailable)
            throw new ConversionException("Para HEIC instala «Extensiones de vídeo HEVC» desde Microsoft Store");
        var png = image.ToByteArray(MagickFormat.Png);
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(png.AsBuffer());
        input.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(input);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        using var output = new InMemoryRandomAccessStream();
        var props = new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(0.85, Windows.Foundation.PropertyType.Single) };
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.HeifEncoderId, output, props);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        output.Seek(0);
        var bytes = new byte[output.Size];
        await output.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        await File.WriteAllBytesAsync(path, bytes);
    }
}
