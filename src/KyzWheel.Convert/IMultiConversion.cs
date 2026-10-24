using KyzWheel.Core.Files;

namespace KyzWheel.Convert;

public interface IMultiConversion : IConversion
{
    Task<ConversionResult> RunManyAsync(IReadOnlyList<string> paths, IProgress<double>? progress, CancellationToken ct);
}
