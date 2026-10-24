using KyzWheel.Core.Files;

namespace KyzWheel.Convert;

public enum ConverterPage { Images, Tools, Media, Documents }

public interface IConversion
{
    string Id { get; }
    string Label { get; }
    string Glyph { get; }
    ConverterPage Page { get; }
    bool CanHandle(string path);
    Task<ConversionResult> RunAsync(string path, IProgress<double>? progress, CancellationToken ct);
}

public sealed class ConversionException(string message, Exception? inner = null) : Exception(message, inner);
