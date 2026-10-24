using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KyzWheel.Core.Clipboard;

public enum ClipKind { Text, Image, Files }

public sealed record ClipItem(string Id, ClipKind Kind, string Hash, DateTimeOffset At, string? Text, string? ImagePath, IReadOnlyList<string>? Files, bool Pinned)
{
    public string Preview => Kind switch
    {
        ClipKind.Text => Text!.Length > 140 ? Text[..140] + "…" : Text,
        ClipKind.Image => "Imagen",
        _ => Files!.Count == 1 ? Path.GetFileName(Files[0]) : $"{Files!.Count} archivos",
    };
}

[JsonSourceGenerationOptions(WriteIndented = false, UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<ClipItem>))]
internal sealed partial class ClipJson : JsonSerializerContext;

public sealed class ClipHistory(string? file, int limit = 25, TimeProvider? clock = null)
{
    public const int MaxTextChars = 100_000;
    private readonly List<ClipItem> _items = Load(file);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _gate = new();

    public event Action? Changed;

    public IReadOnlyList<ClipItem> Items
    {
        get { lock (_gate) return _items.OrderByDescending(i => i.Pinned).ThenByDescending(i => i.At).ToList(); }
    }

    public static string HashOf(string s) => System.Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..16];

    public static string HashOf(byte[] b) => System.Convert.ToHexStringLower(SHA256.HashData(b))[..16];

    public ClipItem? AddText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextChars) return null;
        return Add(ClipKind.Text, HashOf(text), text, null, null);
    }

    public ClipItem? AddImage(string hash, string imagePath) => Add(ClipKind.Image, hash, null, imagePath, null);

    public ClipItem? AddFiles(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return null;
        return Add(ClipKind.Files, HashOf(string.Join('|', files)), null, null, files);
    }

    private ClipItem Add(ClipKind kind, string hash, string? text, string? image, IReadOnlyList<string>? files)
    {
        ClipItem item;
        lock (_gate)
        {
            var existing = _items.FindIndex(i => i.Hash == hash);
            var pinned = existing >= 0 && _items[existing].Pinned;
            if (existing >= 0) _items.RemoveAt(existing);
            item = new ClipItem(Guid.NewGuid().ToString("N")[..10], kind, hash, _clock.GetUtcNow(), text, image, files, pinned);
            _items.Add(item);
            Trim();
        }
        Save();
        Changed?.Invoke();
        return item;
    }

    private void Trim()
    {
        var loose = _items.Where(i => !i.Pinned).OrderByDescending(i => i.At).Skip(limit).ToList();
        foreach (var r in loose)
        {
            _items.Remove(r);
            if (r.ImagePath is { } p) TryDelete(p);
        }
    }

    public void TogglePin(string id)
    {
        lock (_gate)
        {
            var i = _items.FindIndex(x => x.Id == id);
            if (i < 0) return;
            _items[i] = _items[i] with { Pinned = !_items[i].Pinned };
            Trim();
        }
        Save();
        Changed?.Invoke();
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            var i = _items.FindIndex(x => x.Id == id);
            if (i < 0) return;
            if (_items[i].ImagePath is { } p) TryDelete(p);
            _items.RemoveAt(i);
        }
        Save();
        Changed?.Invoke();
    }

    public void ClearUnpinned()
    {
        lock (_gate)
        {
            foreach (var r in _items.Where(i => !i.Pinned).ToList())
            {
                if (r.ImagePath is { } p) TryDelete(p);
                _items.Remove(r);
            }
        }
        Save();
        Changed?.Invoke();
    }

    private static void TryDelete(string p)
    {
        try { File.Delete(p); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static List<ClipItem> Load(string? file)
    {
        if (file is null || !File.Exists(file)) return [];
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(file), ClipJson.Default.ListClipItem) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save()
    {
        if (file is null) return;
        List<ClipItem> copy;
        lock (_gate) copy = _items.ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(copy, ClipJson.Default.ListClipItem));
        File.Move(file + ".tmp", file, true);
    }
}
