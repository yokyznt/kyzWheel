using ImageMagick;
using KyzWheel.Convert.Images;

namespace KyzWheel.Convert.Tests;

public sealed class AnnotationsTests
{
    [Fact]
    public void Arrow_has_shaft_and_two_wings_ending_at_tip()
    {
        var segs = Annotations.ArrowSegments(new PointD(0, 0), new PointD(100, 0), 4);
        Assert.Equal(3, segs.Count);
        Assert.All(segs.Skip(1), s => Assert.Equal(new PointD(100, 0), s.A));
        Assert.All(segs.Skip(1), s => Assert.True(s.B.X < 100));
        Assert.True(segs[1].B.Y * segs[2].B.Y < 0);
    }

    [Fact]
    public void Crop_ratio_keeps_proportion_in_any_direction()
    {
        var b = Annotations.Constrain(new PointD(10, 10), new PointD(170, 0), 16.0 / 9);
        Assert.Equal(170, b.X);
        Assert.Equal(10 - 90, b.Y, 3);
        Assert.Equal(new PointD(50, 70), Annotations.Constrain(new PointD(10, 10), new PointD(50, 70), 0));
        Assert.Equal((10.0, 5.0, 40.0, 20.0), Annotations.Norm(new PointD(50, 25), new PointD(10, 5)));
    }

    [Fact]
    public void Pixelate_flattens_only_the_region()
    {
        using var img = new MagickImage(MagickColors.White, 200, 200);
        for (var x = 0; x < 200; x += 2) new ImageMagick.Drawing.Drawables().FillColor(MagickColors.Black).Rectangle(x, 0, x, 199).Draw(img);
        Annotations.Pixelate(img, 0, 0, 100, 100);
        using var px = img.GetPixels();
        int Changes(int y, int from) => Enumerable.Range(from, 99).Count(x => px.GetPixel(x, y).GetChannel(0) != px.GetPixel(x + 1, y).GetChannel(0));
        Assert.True(Changes(50, 0) <= 15);
        Assert.Equal(99, Changes(150, 100));
    }

    [Fact]
    public void Burn_draws_each_mark_in_its_color()
    {
        using var img = new MagickImage(MagickColors.White, 400, 300);
        Annotations.Burn(img, [
            new Mark(MarkKind.Box, [new PointD(20, 20), new PointD(120, 120)], "#FF0000"),
            new Mark(MarkKind.Arrow, [new PointD(200, 150), new PointD(380, 150)], "#0000FF"),
            new Mark(MarkKind.Pen, [new PointD(20, 250), new PointD(100, 260), new PointD(180, 250)], "#00FF00"),
            new Mark(MarkKind.Text, [new PointD(220, 30)], "#000000", "Hola"),
        ]);
        using var px = img.GetPixels();
        IMagickColor<byte> At(int x, int y) => px.GetPixel(x, y).ToColor()!;
        Assert.True(At(20, 70).R > 200 && At(20, 70).B < 60);
        Assert.True(At(300, 150).B > 200 && At(300, 150).R < 60);
        Assert.True(At(100, 260).G > 200 && At(100, 260).R < 60);
        Assert.Contains(Enumerable.Range(220, 120).SelectMany(x => Enumerable.Range(30, 40).Select(y => At(x, y).R)), v => v < 80);
        Assert.True(At(300, 280).R > 240);
    }
}
