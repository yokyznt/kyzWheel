using KyzWheel.Core.Clipboard;
using Microsoft.Extensions.Time.Testing;

namespace KyzWheel.Core.Tests;

public sealed class ClipHistoryTests
{
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.Parse("2026-10-24T10:00:00Z"));

    private ClipHistory New(string? file = null, int limit = 25) => new(file, limit, _clock);

    [Fact]
    public void Newest_first()
    {
        var h = New();
        h.AddText("uno");
        _clock.Advance(TimeSpan.FromSeconds(1));
        h.AddText("dos");
        Assert.Equal(["dos", "uno"], h.Items.Select(i => i.Text));
    }

    [Fact]
    public void Same_text_moves_to_top_without_duplicate()
    {
        var h = New();
        h.AddText("a");
        _clock.Advance(TimeSpan.FromSeconds(1));
        h.AddText("b");
        _clock.Advance(TimeSpan.FromSeconds(1));
        h.AddText("a");
        Assert.Equal(["a", "b"], h.Items.Select(i => i.Text));
    }

    [Fact]
    public void Limit_does_not_count_pinned()
    {
        var h = New(limit: 3);
        var keep = h.AddText("fijo")!;
        h.TogglePin(keep.Id);
        for (var i = 0; i < 5; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            h.AddText("t" + i);
        }
        Assert.Equal(4, h.Items.Count);
        Assert.Equal("fijo", h.Items[0].Text);
        Assert.Equal(["t4", "t3", "t2"], h.Items.Skip(1).Select(i => i.Text));
    }

    [Fact]
    public void Ignores_blank_and_huge_text()
    {
        var h = New();
        Assert.Null(h.AddText("   "));
        Assert.Null(h.AddText(new string('x', ClipHistory.MaxTextChars + 1)));
        Assert.Empty(h.Items);
    }

    [Fact]
    public void Files_preview_and_clear_keeps_pinned()
    {
        var h = New();
        var f = h.AddFiles([@"C:\a\foto.png", @"C:\a\b.pdf"])!;
        Assert.Equal("2 archivos", f.Preview);
        var t = h.AddText("guardado")!;
        h.TogglePin(t.Id);
        h.ClearUnpinned();
        Assert.Equal(["guardado"], h.Items.Select(i => i.Text));
    }

    [Fact]
    public void Persists_roundtrip()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("kyzclip").FullName, "clips.json");
        var h = New(file);
        h.AddText("persistente");
        Assert.Equal("persistente", New(file).Items.Single().Text);
    }
}
