using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KyzWheel.Core.Text;

public sealed record TranslationResult(string Text, string Source);

public static partial class Translation
{
    [GeneratedRegex(@"[áéíóúñ¿¡]|\b(que|de|el|la|los|las|y|es|en|un|una|por|para|con|no|lo|como|pero|más|está|quiero|hola)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Spanish();

    public static string TargetFor(string text) => Spanish().Matches(text).Count >= 2 ? "en" : "es";

    public static string Url(string text, string target)
        => $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={target}&dt=t&q={Uri.EscapeDataString(text)}";

    public static TranslationResult Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var sb = new StringBuilder();
        if (root[0].ValueKind == JsonValueKind.Array)
            foreach (var seg in root[0].EnumerateArray())
                if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0 && seg[0].ValueKind == JsonValueKind.String) sb.Append(seg[0].GetString());
        var src = root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String ? root[2].GetString()! : "";
        return new TranslationResult(sb.ToString(), src);
    }
}
