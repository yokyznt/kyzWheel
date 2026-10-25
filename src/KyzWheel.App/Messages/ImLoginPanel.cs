using System.Diagnostics;
using KyzWheel.App.Shell;
using KyzWheel.App.WhatsApp;
using KyzWheel.Core.Messaging;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KyzWheel.App.Messages;

public sealed class ImLoginPanel : Grid
{
    private readonly WhatsAppBridge _wa;
    private readonly ImService _svc;
    private readonly Action _opened;
    private readonly StackPanel _body = new() { Spacing = 12 };
    private readonly TextBlock _note = Ui.Label("", 12, Ui.Tertiary, maxLines: 4);

    public ImLoginPanel(WhatsAppBridge wa, ImService svc, Action opened)
    {
        _wa = wa;
        _svc = svc;
        _opened = opened;
        Children.Add(new ScrollViewer { Content = _body, Padding = new Thickness(0, 0, 14, 0) });
        wa.ImStatusChanged += OnStatus;
        Unloaded += (_, _) => wa.ImStatusChanged -= OnStatus;
        Render(wa.Im.TryGetValue(svc.Id, out var st) ? st : new ImStatus(svc.Id, "off", null, null, null, null, null));
    }

    private void OnStatus(ImStatus st)
    {
        if (st.Svc != _svc.Id) return;
        if (st.Open)
        {
            _opened();
            return;
        }
        Render(st);
    }

    private static TextBlock P(string t, double size = 12.5) => Ui.Label(t, size, Ui.Secondary, maxLines: 12);

    private async Task Do(Func<Task> act)
    {
        _note.Text = "Un momento…";
        try
        {
            await act();
            _note.Text = "";
        }
        catch (Exception e)
        {
            _note.Text = e.Message;
        }
    }

    private void Render(ImStatus st)
    {
        _body.Children.Clear();
        _body.Children.Add(Ui.Label("Conectar " + _svc.Name, 17, Ui.Text, bold: true));
        switch (st.State)
        {
            case "qr" when st.Qr is { } qr:
                _body.Children.Add(P(_svc.Id == "telegram"
                    ? "En tu celular abre Telegram › Ajustes › Dispositivos › Vincular dispositivo de escritorio y escanea este código."
                    : "En tu celular abre Discord, toca tu foto › Escanear código QR (o el ícono de QR en Ajustes) y escanea este código."));
                _body.Children.Add(new Border { Background = Ui.Brush(Colors.White), CornerRadius = new CornerRadius(14), Padding = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, Child = new Image { Width = 220, Height = 220, Source = FromDataUrl(qr) } });
                break;
            case "need" when st.Need == "apiid":
                {
                    _body.Children.Add(P("Telegram pide una llave gratuita que se crea una sola vez:\n1. Abre my.telegram.org y entra con tu número.\n2. Ve a «API development tools» y crea una app (nombre: kyzWheel, plataforma: Desktop).\n3. Copia aquí «App api_id» y «App api_hash»."));
                    _body.Children.Add(Ui.Primary("Abrir my.telegram.org", () => Process.Start(new ProcessStartInfo("https://my.telegram.org/apps") { UseShellExecute = true }), "\uE774"));
                    var id = new TextBox { Header = "App api_id", PlaceholderText = "1234567" };
                    var hash = new PasswordBox { Header = "App api_hash" };
                    _body.Children.Add(id);
                    _body.Children.Add(hash);
                    _body.Children.Add(Ui.Primary("Guardar y continuar", () => _ = Do(() => _wa.ImLoginAsync(new { svc = _svc.Id, apiId = id.Text.Trim(), apiHash = hash.Password.Trim() })), "\uE73E"));
                    break;
                }
            case "need" when st.Need == "web":
            case "off" when _svc.Id == "instagram":
            case "error" when _svc.Id == "instagram":
                {
                    if (st.State == "error") _body.Children.Add(Ui.Label(st.Error ?? "Algo falló", 12.5, Ui.Parse("#FCA5A5"), maxLines: 6));
                    _body.Children.Add(P("Se abre la página oficial de Instagram. Entras como siempre (con tu código si tienes verificación en dos pasos) y kyzWheel solo guarda la sesión, nunca tu contraseña.\n\nInstagram no permite esto oficialmente: hay riesgo de que te pida verificar la cuenta o la bloquee un tiempo."));
                    _body.Children.Add(Ui.Primary("Entrar con Instagram", OpenWeb, "\uE71B"));
                    break;
                }
            case "need":
                {
                    _body.Children.Add(P(st.Need == "password" ? "Tu cuenta tiene verificación en dos pasos. Escribe tu contraseña de Telegram" + (string.IsNullOrEmpty(st.Hint) ? "." : $" (pista: {st.Hint}).") : st.Hint ?? "Escribe el código."));
                    Control input = st.Need == "password" ? new PasswordBox { Header = "Contraseña" } : new TextBox { Header = "Código", PlaceholderText = "123456" };
                    _body.Children.Add(input);
                    _body.Children.Add(Ui.Primary("Continuar", () => _ = Do(() => _wa.ImSubmitAsync(_svc.Id, input is PasswordBox pb ? pb.Password : ((TextBox)input).Text.Trim())), "\uE73E"));
                    break;
                }
            case "connecting":
                _body.Children.Add(P(st.Hint ?? "Conectando…"));
                _body.Children.Add(new ProgressRing { IsActive = true, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Left });
                break;
            default:
                if (st.State == "error") _body.Children.Add(Ui.Label(st.Error ?? "Algo falló", 12.5, Ui.Parse("#FCA5A5"), maxLines: 6));
                _body.Children.Add(P(_svc.Id switch
                {
                    "telegram" => "Conexión oficial de Telegram: lees, respondes y mandas stickers desde la rueda.",
                    "discord" => "Lees y respondes tus mensajes directos y menciones. Discord no permite esto oficialmente: hay riesgo de que bloquee la cuenta.",
                    _ => "Lees y respondes tus mensajes directos de Instagram.",
                }));
                _body.Children.Add(Ui.Primary(st.State == "error" ? "Intentar otra vez" : "Conectar", () => _ = Do(() => _wa.ImLoginAsync(new { svc = _svc.Id })), "\uE71B"));
                break;
        }
        _body.Children.Add(_note);
    }

    private void OpenWeb()
    {
        var w = new InstagramLoginWindow();
        w.LoggedIn += cookies => _ = Do(() => _wa.ImLoginAsync(new { svc = "instagram", cookies }));
        w.CenterOnCursorMonitor();
        w.Activate();
    }

    private static BitmapImage FromDataUrl(string dataUrl)
    {
        var bytes = System.Convert.FromBase64String(dataUrl[(dataUrl.IndexOf(',') + 1)..]);
        var bmp = new BitmapImage();
        using var ms = new MemoryStream(bytes);
        bmp.SetSource(ms.AsRandomAccessStream());
        return bmp;
    }
}
