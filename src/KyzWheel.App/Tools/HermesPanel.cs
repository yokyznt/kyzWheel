using System.Diagnostics;
using System.Text;
using KyzWheel.App.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KyzWheel.App.Tools;

public sealed class HermesPanel : Grid
{
    private readonly TextBox _q = new() { PlaceholderText = "Pregúntale a Hermes…", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxHeight = 140, CornerRadius = new CornerRadius(10) };
    private readonly TextBlock _answer = Ui.CopyLine(new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 13, FontFamily = Ui.Font });
    private readonly ProgressRing _ring = new() { IsActive = false, Width = 18, Height = 18 };
    private readonly Button _ask;
    private readonly IReadOnlyList<string> _files;
    private CancellationTokenSource? _cts;

    public static string? Exe()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var p in new[] { Path.Combine(local, "hermes", "bin", "hermes.exe"), Path.Combine(local, "hermes", "hermes-agent", "venv", "Scripts", "hermes.exe") })
            if (File.Exists(p)) return p;
        return null;
    }

    public HermesPanel(IReadOnlyList<string> files)
    {
        _files = files;
        RowSpacing = 10;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        if (files.Count > 0) _q.Header = files.Count == 1 ? "Sobre: " + Path.GetFileName(files[0]) : $"Sobre {files.Count} archivos";
        Children.Add(_q);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _ask = Ui.Primary("Preguntar", () => _ = AskAsync(), "\uE724");
        bar.Children.Add(_ask);
        bar.Children.Add(Ui.IconButton("\uE8C8", "Copiar respuesta", () => { if (_answer.Text.Length > 0) ScreenTools.CopyText(_answer.Text); }));
        bar.Children.Add(_ring);
        SetRow(bar, 1);
        Children.Add(bar);
        var scroll = new ScrollViewer { Content = _answer };
        SetRow(scroll, 2);
        Children.Add(scroll);
        _q.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter && !Ui.ShiftDown())
            {
                e.Handled = true;
                _ = AskAsync();
            }
        };
        Loaded += (_, _) => _q.Focus(FocusState.Programmatic);
        Unloaded += (_, _) => _cts?.Cancel();
    }

    private async Task AskAsync()
    {
        var q = _q.Text.Trim();
        if (q.Length == 0) return;
        if (Exe() is not { } exe)
        {
            _answer.Text = "No encontré Hermes en esta PC.";
            return;
        }
        if (_files.Count > 0) q += "\n\nArchivos: " + string.Join(", ", _files.Select(f => $"\"{f}\""));
        _ask.IsEnabled = false;
        _ring.IsActive = true;
        _answer.Text = "Pensando…";
        _cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
            foreach (var a in new[] { "chat", "-q", q, "--oneshot", "-Q" }) psi.ArgumentList.Add(a);
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync(_cts.Token);
            await p.WaitForExitAsync(_cts.Token);
            var text = (await output).Trim();
            _answer.Text = text.Length > 0 ? text : "Hermes no devolvió respuesta.";
        }
        catch (OperationCanceledException)
        {
            _answer.Text = "Se tardó demasiado.";
        }
        catch (Exception e)
        {
            _answer.Text = "No se pudo preguntar: " + e.Message;
        }
        finally
        {
            _ask.IsEnabled = true;
            _ring.IsActive = false;
        }
    }
}
