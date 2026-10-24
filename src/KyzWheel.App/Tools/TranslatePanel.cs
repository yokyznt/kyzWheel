using KyzWheel.App.Shell;
using KyzWheel.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KyzWheel.App.Tools;

public sealed class TranslatePanel : StackPanel
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly TextBox _src = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, MaxHeight = 160, CornerRadius = new CornerRadius(10) };
    private readonly TextBlock _out = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 14, FontFamily = Ui.Font };
    private readonly TextBlock _meta;
    private readonly Func<string, Task>? _toPhone;
    private string _target = "es";

    public TranslatePanel(string text, Func<string, Task>? toPhone)
    {
        _toPhone = toPhone;
        Spacing = 10;
        _src.Text = text;
        _meta = Ui.Label("", 11.5, Ui.Tertiary);
        Children.Add(_src);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        bar.Children.Add(Ui.Primary("Traducir", () => _ = RunAsync(), "\uE8C1"));
        bar.Children.Add(Ui.IconButton("\uE8AB", "Cambiar idioma", () => { _target = _target == "es" ? "en" : "es"; _ = RunAsync(_target); }));
        bar.Children.Add(Ui.IconButton("\uE8C8", "Copiar traducción", () => { if (_out.Text.Length > 0) ScreenTools.CopyText(_out.Text); _meta.Text = "Copiado"; }));
        if (toPhone is not null) bar.Children.Add(Ui.IconButton("\uE8EA", "Mandar al celular", () => { if (_out.Text.Length > 0) _ = _toPhone!(_out.Text); }));
        Children.Add(bar);
        Children.Add(new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Background = Ui.Brush(Microsoft.UI.ColorHelper.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), Child = _out });
        Children.Add(_meta);
        if (text.Length > 0) _ = RunAsync();
    }

    private async Task RunAsync(string? forced = null)
    {
        var text = _src.Text.Trim();
        if (text.Length == 0) return;
        _target = forced ?? KyzWheel.Core.Text.Translation.TargetFor(text);
        _meta.Text = "Traduciendo…";
        try
        {
            var r = KyzWheel.Core.Text.Translation.Parse(await Http.GetStringAsync(KyzWheel.Core.Text.Translation.Url(text.Length > 4500 ? text[..4500] : text, _target)));
            _out.Text = r.Text;
            _meta.Text = $"{(r.Source.Length > 0 ? r.Source : "auto")} → {_target}";
        }
        catch (Exception e)
        {
            _meta.Text = "No se pudo traducir: " + e.Message;
        }
    }
}
