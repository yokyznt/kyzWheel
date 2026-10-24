using System.Text.Json;
using KyzWheel.Core.Input;

namespace KyzWheel.Core.Config;

public sealed class ConfigStore(string path)
{
    public string FilePath { get; } = path;

    public static string DefaultPath => Environment.GetEnvironmentVariable("KYZ_CONFIG") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "kyzWheel", "config.json");

    public AppConfig Load()
    {
        if (!File.Exists(FilePath))
        {
            Save(AppConfig.Default);
            return AppConfig.Default;
        }
        var raw = File.ReadAllText(FilePath);
        AppConfig? cfg = null;
        try
        {
            cfg = JsonSerializer.Deserialize(raw, ConfigJson.Default.AppConfig);
        }
        catch (JsonException)
        {
        }
        if (cfg is not null && cfg.Slots is not null && Validate(cfg).Count == 0) return cfg;
        File.WriteAllText(FilePath + ".bak", raw);
        Save(AppConfig.Default);
        return AppConfig.Default;
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, ConfigJson.Default.AppConfig));
        File.Move(tmp, FilePath, true);
    }

    public static IReadOnlyList<string> Validate(AppConfig config)
    {
        var errors = new List<string>();
        if (!Hotkey.TryParse(config.GlobalHotkey, out var global) || Hotkey.IsReserved(global))
            errors.Add($"Atajo global inválido: {config.GlobalHotkey}");
        var slots = config.Slots ?? [];
        if (slots.Count < AppConfig.MinSlots || slots.Count > AppConfig.MaxSlots)
            errors.Add($"Debe haber entre {AppConfig.MinSlots} y {AppConfig.MaxSlots} secciones");
        foreach (var dup in slots.GroupBy(s => s.Id).Where(g => g.Count() > 1))
            errors.Add($"Id repetido: {dup.Key}");
        var used = new HashSet<string>();
        if (Hotkey.TryParse(config.GlobalHotkey, out var g)) used.Add(g.ToString());
        foreach (var s in slots.Where(s => !string.IsNullOrWhiteSpace(s.Hotkey)))
        {
            if (!Hotkey.TryParse(s.Hotkey!, out var h) || Hotkey.IsReserved(h))
                errors.Add($"Atajo inválido en {s.Name}: {s.Hotkey}");
            else if (!used.Add(h.ToString()))
                errors.Add($"Atajo repetido: {h}");
        }
        return errors;
    }
}
