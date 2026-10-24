namespace KyzWheel.Core.Files;

public sealed record ConversionResult(string SourcePath, string OutputPath, long SizeBefore, long SizeAfter);
