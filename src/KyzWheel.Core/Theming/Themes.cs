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
    string Glow)
{
    public string Font { get; init; } = "Outfit";
    public double Gap { get; init; } = 9;
    public double CornerOut { get; init; } = 24;
    public double CornerIn { get; init; } = 15;
    public bool Upper { get; init; } = true;
    public int Spacing { get; init; } = 15;
    public string Tagline { get; init; } = "";
}

public static class Themes
{
    public const string DefaultId = "liquid";

    public static IReadOnlyList<ThemePalette> All { get; } =
    [
        new("liquid", "Liquid Glass", "#7DD3FC", "#24FFFFFF", "#06FFFFFF", "#3DFFFFFF", "#30FFFFFF", "#1CFFFFFF", "#05FFFFFF", "#70FFFFFF", "#9905070D", "#40FFFFFF", "#337DD3FC") { Tagline = "Vidrio claro, pétalos suaves" },
        new("obsidian", "Obsidiana", "#FF8A3D", "#CC1B1B22", "#E00D0D12", "#1CFFFFFF", "#12FFFFFF", "#E017171E", "#F009090D", "#38FFFFFF", "#B308080C", "#1FFFFFFF", "#66000000") { Font = "Segoe UI Variable Display", Gap = 6, CornerOut = 12, CornerIn = 8, Spacing = 60, Tagline = "Piedra negra, cortes precisos" },
        new("tangerine", "Tangerine", "#FF8A3D", "#40FF9A55", "#10FF6A1A", "#5CFFB27A", "#38FFE2CC", "#30FF9A55", "#0CFF6A1A", "#80FFB27A", "#991A0E06", "#4DFF8A3D", "#55FF8A3D") { Gap = 13, CornerOut = 34, CornerIn = 22, Upper = false, Spacing = 0, Tagline = "Gajos redondos como fruta" },
        new("neon", "Cyber Neón", "#22D3EE", "#337C3AED", "#0A22D3EE", "#AA22D3EE", "#30F472B6", "#267C3AED", "#0822D3EE", "#CC22D3EE", "#99080418", "#7722D3EE", "#88A855F7") { Font = "Cascadia Code", Gap = 4, CornerOut = 3, CornerIn = 2, Spacing = 30, Tagline = "Terminal futurista, bordes que brillan" },
        new("emerald", "Esmeralda", "#34D399", "#3334D399", "#0C065F46", "#5534D399", "#30A7F3D0", "#2834D399", "#08065F46", "#7034D399", "#99041610", "#4D34D399", "#4434D399") { Font = "Bahnschrift", Gap = 8, CornerOut = 18, CornerIn = 10, Spacing = 40, Tagline = "Gema tallada, letra técnica" },
        new("sunset", "Atardecer", "#FB7185", "#40FB7185", "#14FDBA74", "#55FDA4AF", "#38FDBA74", "#30FB7185", "#10FDBA74", "#80FDA4AF", "#991A0810", "#4DFB7185", "#55FB7185") { Gap = 16, CornerOut = 40, CornerIn = 26, Upper = false, Spacing = 10, Tagline = "Burbujas cálidas" },
        new("midnight", "Medianoche", "#818CF8", "#2E818CF8", "#0A1E1B4B", "#33C7D2FE", "#20C7D2FE", "#22818CF8", "#081E1B4B", "#55A5B4FC", "#99080B20", "#33818CF8", "#30818CF8") { Font = "Segoe UI Variable Display", Gap = 2, CornerOut = 2, CornerIn = 2, Upper = false, Spacing = 20, Tagline = "Líneas finas, minimalista" },
        new("aurora", "Aurora", "#5EEAD4", "#4434D399", "#228B5CF6", "#665EEAD4", "#40C4B5FD", "#3334D399", "#228B5CF6", "#995EEAD4", "#99061218", "#555EEAD4", "#665EEAD4") { Gap = 10, CornerOut = 28, CornerIn = 18, Spacing = 25, Tagline = "Verde a violeta, como el cielo polar" },
        new("sakura", "Sakura", "#F9A8D4", "#40F9A8D4", "#14FBCFE8", "#66FBCFE8", "#40FFFFFF", "#33F9A8D4", "#10FBCFE8", "#99FBCFE8", "#991A0A14", "#55F9A8D4", "#55F9A8D4") { Font = "Segoe UI Variable Display", Gap = 12, CornerOut = 30, CornerIn = 20, Upper = false, Spacing = 30, Tagline = "Pétalos rosas, suave" },
        new("arcade", "Arcade", "#FACC15", "#55FACC15", "#22EC4899", "#AAFACC15", "#40FFFFFF", "#44EC4899", "#22FACC15", "#CCFACC15", "#99140A1A", "#88EC4899", "#88EC4899") { Font = "Bahnschrift", Gap = 7, CornerOut = 6, CornerIn = 4, Spacing = 80, Tagline = "Maquinita de los 80" },
        new("carbon", "Carbono", "#E5E7EB", "#30FFFFFF", "#18FFFFFF", "#22FFFFFF", "#10FFFFFF", "#26FFFFFF", "#12FFFFFF", "#44FFFFFF", "#CC0A0A0A", "#26FFFFFF", "#22FFFFFF") { Font = "Cascadia Code", Gap = 3, CornerOut = 8, CornerIn = 5, Upper = false, Spacing = 0, Tagline = "Monocromo para programadores" },
        new("lava", "Lava", "#F97316", "#66DC2626", "#22F59E0B", "#99F97316", "#40FDE68A", "#55DC2626", "#22F59E0B", "#CCF97316", "#99180604", "#88F97316", "#99DC2626") { Font = "Bahnschrift", Gap = 11, CornerOut = 22, CornerIn = 6, Spacing = 50, Tagline = "Roca fundida, brillo intenso" },
    ];

    public static ThemePalette Find(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];
}
