using System.Collections.Concurrent;
using System.Diagnostics;
using KyzWheel.Platform.Files;
using Microsoft.Toolkit.Uwp.Notifications;

namespace KyzWheel.App.Shell;

public sealed class Toasts : IDisposable
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _outputs = new();
    private bool _ready;

    public void Init()
    {
        try
        {
            ToastNotificationManagerCompat.OnActivated += e => OnInvoked(ToastArguments.Parse(e.Argument));
            _ready = true;
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    private void Show(ToastContentBuilder b)
    {
        if (!_ready) return;
        try
        {
            b.Show();
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    public void Info(string title, string body) => Show(new ToastContentBuilder().AddText(title).AddText(body));

    public void Result(string title, string body, IReadOnlyList<string> outputs)
    {
        var key = Guid.NewGuid().ToString("N");
        _outputs[key] = outputs;
        var b = new ToastContentBuilder().AddText(title).AddText(body);
        if (outputs.Count == 1)
            b.AddButton(new ToastButton().SetContent("Abrir").AddArgument("a", "open").AddArgument("k", key).SetBackgroundActivation());
        if (outputs.Count > 0)
        {
            b.AddButton(new ToastButton().SetContent("Mostrar en carpeta").AddArgument("a", "folder").AddArgument("k", key).SetBackgroundActivation());
            b.AddButton(new ToastButton().SetContent("Deshacer").AddArgument("a", "undo").AddArgument("k", key).SetBackgroundActivation());
        }
        Show(b);
    }

    private void OnInvoked(ToastArguments args)
    {
        if (!args.TryGetValue("k", out var key) || !_outputs.TryGetValue(key, out var outs)) return;
        args.TryGetValue("a", out var action);
        try
        {
            switch (action)
            {
                case "open":
                    Process.Start(new ProcessStartInfo(outs[0]) { UseShellExecute = true });
                    break;
                case "folder":
                    Process.Start("explorer.exe", $"/select,\"{outs[0]}\"");
                    break;
                case "undo":
                    RecycleBin.Send(outs);
                    _outputs.TryRemove(key, out _);
                    Info("Deshecho", outs.Count == 1 ? "El archivo se movió a la Papelera" : $"{outs.Count} archivos se movieron a la Papelera");
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    public void Dispose()
    {
    }
}
