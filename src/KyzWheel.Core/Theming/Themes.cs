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
        new("liquid", "Liquid Glass", "#7DD3FC", "#24FFFFFF", "#06FFFFFF", "#3DFFFFFF", "#30FFFFFF", "#1CFFFFFF", "#05FFFFFF", "#70FFFFFF", "#9905070D", "#40FFFFFF", "#337DD3FC"),
        new("obsidian", "Obsidiana", "#FF8A3D", "#CC1B1B22", "#E00D0D12", "#1CFFFFFF", "#12FFFFFF", "#E017171E", "#F009090D", "#38FFFFFF", "#B308080C", "#1FFFFFFF", "#66000000"),
        new("tangerine", "Tangerine", "#FF8A3D", "#33FF9A55", "#0AFF6A1A", "#4DFFB27A", "#30FFE2CC", "#26FF9A55", "#08FF6A1A", "#70FFB27A", "#991A0E06", "#4DFF8A3D", "#40FF8A3D"),
        new("neon", "Cyber Neón", "#22D3EE", "#337C3AED", "#0A22D3EE", "#5522D3EE", "#30F472B6", "#267C3AED", "#0822D3EE", "#8822D3EE", "#99080418", "#5522D3EE", "#55A855F7"),
        new("emerald", "Esmeralda", "#34D399", "#2E34D399", "#0A065F46", "#4D34D399", "#30A7F3D0", "#2234D399", "#08065F46", "#7034D399", "#99041610", "#4D34D399", "#4034D399"),
        new("sunset", "Atardecer", "#FB7185", "#2EFB7185", "#0AFDBA74", "#4DFB7185", "#30FDBA74", "#22FB7185", "#08FDBA74", "#70FDA4AF", "#991A0810", "#4DFB7185", "#40FB7185"),
        new("midnight", "Medianoche", "#818CF8", "#2E818CF8", "#0A1E1B4B", "#4D818CF8", "#30C7D2FE", "#22818CF8", "#081E1B4B", "#70A5B4FC", "#99080B20", "#4D818CF8", "#40818CF8"),
    ];

    public static ThemePalette Find(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];
}
