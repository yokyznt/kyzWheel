namespace KyzWheel.Core.Theming;

public sealed record ThemePalette(
    string Id,
    string Name,
    string Accent,
    string SliceTop,
    string SliceBottom,
    string SliceStroke,
    string Sheen,
    string HubTop,
    string HubBottom,
    string Rim,
    string PanelTint,
    string PanelEdge,
    string Glow);

public static class Themes
{
    public const string DefaultId = "liquid";

    public static IReadOnlyList<ThemePalette> All { get; } =
    [
        new("liquid", "Liquid Glass", "#7DD3FC", "#EB1E2230", "#EB0A0C14", "#33FFFFFF", "#40FFFFFF", "#F0262A38", "#F00B0D16", "#66FFFFFF", "#4D0A0C14", "#40FFFFFF", "#5538BDF8"),
        new("obsidian", "Obsidiana", "#FF8A3D", "#FF1B1B22", "#FF0D0D12", "#1CFFFFFF", "#14FFFFFF", "#FF17171E", "#FF09090D", "#40FFFFFF", "#8C08080C", "#1FFFFFFF", "#99000000"),
        new("tangerine", "Tangerine", "#FF8A3D", "#E6301A10", "#E6140A06", "#33FF8A3D", "#30FFD2B0", "#F03A2214", "#F0120905", "#66FFB27A", "#591A0E06", "#40FF8A3D", "#55FF8A3D"),
        new("neon", "Cyber Neón", "#22D3EE", "#E60F0A24", "#E605030F", "#4022D3EE", "#30F472B6", "#F0170F33", "#F0060312", "#8822D3EE", "#59080418", "#5522D3EE", "#66A855F7"),
        new("emerald", "Esmeralda", "#34D399", "#E60C2219", "#E6040D09", "#3334D399", "#30A7F3D0", "#F0102A20", "#F0040E0A", "#6634D399", "#59041610", "#4034D399", "#5534D399"),
        new("sunset", "Atardecer", "#FB7185", "#E62A0F1A", "#E60F050A", "#33FB7185", "#30FDBA74", "#F0341424", "#F0110609", "#66FDA4AF", "#591A0810", "#40FB7185", "#55FB7185"),
        new("midnight", "Medianoche", "#818CF8", "#E6111633", "#E6060818", "#33818CF8", "#30C7D2FE", "#F0161C40", "#F006081A", "#66A5B4FC", "#59080B20", "#40818CF8", "#55818CF8"),
    ];

    public static ThemePalette Find(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];
}
