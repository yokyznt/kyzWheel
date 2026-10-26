using System.Globalization;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace KyzWheel.App.Shell;

public static class Ui
{
    public static readonly Windows.UI.Color Text = ColorHelper.FromArgb(0xF2, 0xFF, 0xFF, 0xFF);
    public static readonly Windows.UI.Color Secondary = ColorHelper.FromArgb(0xA6, 0xFF, 0xFF, 0xFF);
    public static readonly Windows.UI.Color Tertiary = ColorHelper.FromArgb(0x6B, 0xFF, 0xFF, 0xFF);
    public static readonly Windows.UI.Color Hairline = ColorHelper.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);
    public static readonly Windows.UI.Color RowHover = ColorHelper.FromArgb(0x14, 0xFF, 0xFF, 0xFF);
    public static readonly Windows.UI.Color RowPress = ColorHelper.FromArgb(0x0C, 0xFF, 0xFF, 0xFF);
    public static readonly FontFamily Icons = new("Segoe Fluent Icons,Segoe MDL2 Assets");
    public static readonly FontFamily Font = new("ms-appx:///Assets/Fonts/Outfit.ttf#Outfit");

    private static readonly Dictionary<string, FontFamily> Fonts = new();

    public static FontFamily FontFor(string? name) => string.IsNullOrEmpty(name) || name == "Outfit" ? Font : Fonts.TryGetValue(name, out var f) ? f : Fonts[name] = new FontFamily(name + ",Segoe UI Variable Display,Segoe UI");

    public static Windows.UI.Color Parse(string hex, byte alpha = 0xFF)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 8) h = h[2..];
        var v = System.Convert.ToUInt32(h, 16);
        return ColorHelper.FromArgb(alpha, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static SolidColorBrush Brush(Windows.UI.Color c) => new(c);

    public static TextBlock Label(string text, double size = 13, Windows.UI.Color? color = null, bool bold = false, int maxLines = 1) => new()
    {
        FontFamily = Font,
        Text = text,
        FontSize = size,
        Foreground = Brush(color ?? Text),
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = maxLines == 1 ? TextWrapping.NoWrap : TextWrapping.WrapWholeWords,
        MaxLines = maxLines,
        CharacterSpacing = size >= 18 ? -12 : 0,
    };

    public static FontIcon Icon(string glyph, double size = 16, Windows.UI.Color? color = null) => new()
    {
        Glyph = glyph,
        FontSize = size,
        FontFamily = Icons,
        Foreground = Brush(color ?? Secondary),
    };

    public static Grid Avatar(string name, string colorHex, double size = 36)
    {
        var c = Parse(colorHex);
        var g = new Grid { Width = size, Height = size };
        var fill = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
        fill.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0x55, c.R, c.G, c.B), Offset = 0 });
        fill.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0x22, c.R, c.G, c.B), Offset = 1 });
        g.Children.Add(new Ellipse { Fill = fill, Stroke = Brush(ColorHelper.FromArgb(0x50, c.R, c.G, c.B)), StrokeThickness = 1 });
        g.Children.Add(new TextBlock
        {
            Text = Initials(name),
            FontSize = size * 0.38,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush(ColorHelper.FromArgb(0xFF, (byte)Math.Min(255, c.R + 60), (byte)Math.Min(255, c.G + 60), (byte)Math.Min(255, c.B + 60))),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return g;
    }

    public static string Initials(string name)
    {
        var parts = name.Split([' ', '.', '_', '-', '@'], StringSplitOptions.RemoveEmptyEntries).Where(p => char.IsLetterOrDigit(p[0])).ToList();
        if (parts.Count == 0) return "?";
        var s = parts.Count == 1 ? parts[0][..1] : parts[0][..1] + parts[1][..1];
        return s.ToUpperInvariant();
    }

    public static string When(DateTimeOffset date)
    {
        var local = date.ToLocalTime();
        var now = DateTimeOffset.Now;
        var es = CultureInfo.GetCultureInfo("es-MX");
        if (now - local < TimeSpan.FromMinutes(1)) return "ahora";
        if (now - local < TimeSpan.FromHours(1)) return $"{(int)(now - local).TotalMinutes} min";
        if (local.Date == now.Date) return local.ToString("HH:mm", es);
        if (local.Date == now.Date.AddDays(-1)) return "ayer";
        if (now - local < TimeSpan.FromDays(7)) return local.ToString("ddd", es).TrimEnd('.');
        return local.ToString("d MMM", es).TrimEnd('.');
    }

    public static Border Pill(string text, Windows.UI.Color color) => new()
    {
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(7, 2, 7, 2),
        Background = Brush(ColorHelper.FromArgb(0x26, color.R, color.G, color.B)),
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = Brush(ColorHelper.FromArgb(0xFF, color.R, color.G, color.B)) },
    };

    public static Rectangle Divider() => new() { Height = 1, Fill = Brush(Hairline), Margin = new Thickness(0, 4, 0, 4) };

    public static void Pressable(UIElement element, Border? surface = null)
    {
        var v = ElementCompositionPreview.GetElementVisual(element);
        element.PointerEntered += (_, _) => { if (surface is not null) surface.Background = Brush(RowHover); };
        element.PointerExited += (_, _) =>
        {
            if (surface is not null) surface.Background = Brush(Colors.Transparent);
            Scale(v, 1f, 160);
        };
        element.PointerPressed += (_, _) =>
        {
            if (surface is not null) surface.Background = Brush(RowPress);
            if (element is FrameworkElement fe) v.CenterPoint = new Vector3((float)fe.ActualWidth / 2, (float)fe.ActualHeight / 2, 0);
            Scale(v, 0.98f, 100);
        };
        element.PointerReleased += (_, _) =>
        {
            if (surface is not null) surface.Background = Brush(RowHover);
            Scale(v, 1f, 160);
        };
        element.PointerCanceled += (_, _) => Scale(v, 1f, 160);
    }

    private static void Scale(Visual v, float to, int ms)
    {
        var c = v.Compositor;
        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(1f, new Vector3(to, to, 1f), c.CreateCubicBezierEasingFunction(new Vector2(0.23f, 1f), new Vector2(0.32f, 1f)));
        a.Duration = TimeSpan.FromMilliseconds(ms);
        v.StartAnimation("Scale", a);
    }

    public static Button IconButton(string glyph, string tooltip, Action click)
    {
        var b = new Button
        {
            Content = Icon(glyph, 14),
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Background = Brush(Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(b, tooltip);
        b.Click += (_, _) => click();
        return b;
    }

    public static Button Primary(string text, Action click, string? glyph = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (glyph is not null) content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 13, FontFamily = Icons });
        content.Children.Add(new TextBlock { Text = text });
        var b = new Button { Content = content, Style = (Style)Application.Current.Resources["AccentButtonStyle"], CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 7, 14, 7) };
        b.Click += (_, _) => click();
        return b;
    }

    public static Button Secondary_(string text, Action click, string? glyph = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (glyph is not null) content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 13, FontFamily = Icons });
        content.Children.Add(new TextBlock { Text = text });
        var b = new Button { Content = content, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7) };
        b.Click += (_, _) => click();
        return b;
    }

    public static void ApplyAccent(string hex)
    {
        var c = Parse(hex);
        var r = Application.Current.Resources;
        r["SystemAccentColor"] = c;
        r["SystemAccentColorLight1"] = Lighten(c, 0.15);
        r["SystemAccentColorLight2"] = Lighten(c, 0.3);
        r["SystemAccentColorLight3"] = Lighten(c, 0.45);
        r["SystemAccentColorDark1"] = Lighten(c, -0.15);
        r["SystemAccentColorDark2"] = Lighten(c, -0.3);
        r["SystemAccentColorDark3"] = Lighten(c, -0.45);
    }

    private static Windows.UI.Color Lighten(Windows.UI.Color c, double k)
    {
        byte F(byte v) => (byte)Math.Clamp(k >= 0 ? v + (255 - v) * k : v * (1 + k), 0, 255);
        return ColorHelper.FromArgb(0xFF, F(c.R), F(c.G), F(c.B));
    }

    public static bool ShiftDown() => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    public static event Action<string>? LineCopied;

    public static string LineAt(string text, int index)
    {
        if (string.IsNullOrEmpty(text)) return "";
        index = Math.Clamp(index, 0, text.Length - 1);
        if (text[index] == '\n' && index > 0) index--;
        var start = text.LastIndexOf('\n', index) + 1;
        var end = text.IndexOf('\n', index);
        if (end < 0) end = text.Length;
        return text[start..end].TrimEnd('\r').Trim();
    }

    public static Microsoft.UI.Xaml.Controls.TextBlock CopyLine(Microsoft.UI.Xaml.Controls.TextBlock tb)
    {
        tb.IsTextSelectionEnabled = true;
        var pending = false;
        tb.AddHandler(Microsoft.UI.Xaml.UIElement.DoubleTappedEvent, new Microsoft.UI.Xaml.Input.DoubleTappedEventHandler((_, e) =>
        {
            e.Handled = true;
            if (pending) return;
            pending = true;
            tb.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                pending = false;
                var full = tb.Text ?? "";
                if (full.Length == 0) return;
                var word = tb.SelectedText ?? "";
                var raw = 0;
                try { raw = tb.SelectionStart.Offset - tb.ContentStart.Offset; } catch (Exception) { }
                var shift = 1;
                foreach (var k in new[] { 1, 0, 2 })
                    if (word.Length > 0 && raw - k >= 0 && raw - k + word.Length <= full.Length && string.CompareOrdinal(full, raw - k, word, 0, word.Length) == 0) { shift = k; break; }
                var idx = Math.Clamp(raw - shift, 0, full.Length - 1);
                var line = LineAt(full, idx);
                if (line.Length == 0) return;
                var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dp.SetText(line);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
                try
                {
                    var start = full[idx] == '\n' && idx > 0 ? full.LastIndexOf('\n', idx - 1) + 1 : full.LastIndexOf('\n', idx) + 1;
                    var s0 = full.IndexOf(line, start, StringComparison.Ordinal);
                    if (s0 < 0) s0 = full.IndexOf(line, StringComparison.Ordinal);
                    var p0 = tb.ContentStart.GetPositionAtOffset(s0 + shift, Microsoft.UI.Xaml.Documents.LogicalDirection.Forward);
                    var p1 = tb.ContentStart.GetPositionAtOffset(s0 + shift + line.Length, Microsoft.UI.Xaml.Documents.LogicalDirection.Backward);
                    if (p0 is not null && p1 is not null) tb.Select(p0, p1);
                }
                catch (Exception) { }
                LineCopied?.Invoke(line);
            });
        }), true);
        return tb;
    }
}
