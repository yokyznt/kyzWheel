using KyzWheel.Core.Files;

namespace KyzWheel.Core.Tests;

public sealed class BatchSummaryTests
{
    private static ConversionResult R(long before, long after) => new("a", "b", before, after);

    [Fact]
    public void Many_files_in_mb()
        => Assert.Equal("3 archivos · 4.2 MB → 1.1 MB",
            BatchSummary.Describe([R(1468006, 384000), R(1468006, 384000), R(1468007, 385351)], 0));

    [Fact]
    public void One_file_in_kb()
        => Assert.Equal("1 archivo · 820 KB → 210 KB", BatchSummary.Describe([R(820 * 1024, 210 * 1024)], 0));

    [Fact]
    public void With_failures()
        => Assert.Equal("2 listos, 1 falló", BatchSummary.Describe([R(1, 1), R(1, 1)], 1));

    [Fact]
    public void All_failed()
        => Assert.Equal("0 listos, 2 fallaron", BatchSummary.Describe([], 2));

    [Fact]
    public void Bytes_are_shown_as_b()
        => Assert.Equal("1 archivo · 900 B → 300 B", BatchSummary.Describe([R(900, 300)], 0));
}
