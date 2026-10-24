namespace KyzWheel.Core.Files;

public static class OutputNaming
{
    public static string NextFreePath(string sourcePath, string newExtension)
    {
        var ext = newExtension.StartsWith('.') ? newExtension.ToLowerInvariant() : "." + newExtension.ToLowerInvariant();
        var dir = Path.GetDirectoryName(sourcePath) ?? "";
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        for (var i = 0; ; i++)
        {
            var name = i == 0 ? stem + ext : $"{stem}_{i}{ext}";
            var candidate = Path.Combine(dir, name);
            if (string.Equals(candidate, sourcePath, StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }
}
