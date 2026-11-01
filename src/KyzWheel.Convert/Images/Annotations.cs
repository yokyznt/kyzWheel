using ImageMagick;
using ImageMagick.Drawing;

namespace KyzWheel.Convert.Images;

public enum MarkKind { Arrow, Box, Pen, Text }

public sealed record Mark(MarkKind Kind, IReadOnlyList<PointD> Points, string Color, string? Text = null);

public static class Annotations
{
    public static double StrokeFor(uint width, uint height) => Math.Max(3, Math.Max(width, height) / 220.0);

    public static double TextSize(double stroke) => stroke * 8;

    public static IReadOnlyList<(PointD A, PointD B)> ArrowSegments(PointD a, PointD b, double stroke)
    {
        var ang = Math.Atan2(b.Y - a.Y, b.X - a.X);
        var head = Math.Max(stroke * 4.5, 14);
        PointD Wing(double d) => new(b.X - head * Math.Cos(ang + d), b.Y - head * Math.Sin(ang + d));
        return [(a, b), (b, Wing(0.5)), (b, Wing(-0.5))];
    }

    public static (double X, double Y, double W, double H) Norm(PointD a, PointD b)
        => (Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public static PointD Constrain(PointD a, PointD b, double ratio)
    {
        if (ratio <= 0) return b;
        var dy = b.Y - a.Y;
        return new PointD(b.X, a.Y + Math.Abs(b.X - a.X) / ratio * (dy < 0 ? -1 : 1));
    }

    public static void Pixelate(IMagickImage<byte> image, double x, double y, double w, double h)
    {
        var g = new MagickGeometry((int)Math.Round(x), (int)Math.Round(y), (uint)Math.Max(1, Math.Round(w)), (uint)Math.Max(1, Math.Round(h)));
        using var area = image.CloneArea(g);
        var block = Math.Max(8, (int)(Math.Max(image.Width, image.Height) / 60));
        area.Sample((uint)Math.Max(1, area.Width / block), (uint)Math.Max(1, area.Height / block));
        area.Sample(g.Width, g.Height);
        image.Composite(area, g.X, g.Y, CompositeOperator.Over);
    }

    public static void Burn(IMagickImage<byte> image, IEnumerable<Mark> marks)
    {
        var stroke = StrokeFor(image.Width, image.Height);
        foreach (var m in marks)
        {
            var c = new MagickColor(m.Color);
            var d = new Drawables().StrokeColor(c).StrokeWidth(stroke).FillColor(MagickColors.Transparent).StrokeLineCap(LineCap.Round).StrokeLineJoin(LineJoin.Round);
            switch (m.Kind)
            {
                case MarkKind.Arrow when m.Points.Count == 2:
                    foreach (var (a, b) in ArrowSegments(m.Points[0], m.Points[1], stroke)) d.Line(a.X, a.Y, b.X, b.Y);
                    break;
                case MarkKind.Box when m.Points.Count == 2:
                    var r = Norm(m.Points[0], m.Points[1]);
                    d.Rectangle(r.X, r.Y, r.X + r.W, r.Y + r.H);
                    break;
                case MarkKind.Pen when m.Points.Count > 1:
                    d.Polyline([.. m.Points]);
                    break;
                case MarkKind.Text when m.Points.Count == 1 && !string.IsNullOrEmpty(m.Text):
                    var size = TextSize(stroke);
                    d = new Drawables().FillColor(c).StrokeColor(MagickColors.Transparent).Font("Segoe UI Bold").FontPointSize(size).Text(m.Points[0].X, m.Points[0].Y + size * 0.95, m.Text);
                    break;
                default:
                    continue;
            }
            d.Draw(image);
        }
    }
}
