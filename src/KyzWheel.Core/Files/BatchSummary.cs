using System.Globalization;

namespace KyzWheel.Core.Files;

public static class BatchSummary
{
    public static string Describe(IReadOnlyList<ConversionResult> ok, int failed)
    {
        if (failed > 0)
            return $"{ok.Count} {(ok.Count == 1 ? "listo" : "listos")}, {failed} {(failed == 1 ? "falló" : "fallaron")}";
        var before = ok.Sum(r => r.SizeBefore);
        var after = ok.Sum(r => r.SizeAfter);
        return $"{ok.Count} {(ok.Count == 1 ? "archivo" : "archivos")} · {Size(before)} → {Size(after)}";
    }

    public static string Size(long bytes)
    {
        const double K = 1024;
        if (bytes < K) return $"{bytes} B";
        if (bytes < K * K) return string.Create(CultureInfo.InvariantCulture, $"{Math.Round(bytes / K):0} KB");
        if (bytes < K * K * K) return string.Create(CultureInfo.InvariantCulture, $"{bytes / (K * K):0.0} MB");
        return string.Create(CultureInfo.InvariantCulture, $"{bytes / (K * K * K):0.0} GB");
    }
}
