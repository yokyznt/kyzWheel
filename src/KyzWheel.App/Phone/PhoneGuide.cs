using System.Diagnostics;
using KyzWheel.App.Shell;
using KyzWheel.Core.Phone;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;

namespace KyzWheel.App.Phone;

public sealed class PhoneGuide : Grid
{
    private const string PlayStore = "https://play.google.com/store/apps/details?id=org.kde.kdeconnect_tp";
    private readonly PhoneService _phone;
    private readonly StackPanel _devices = new() { Spacing = 6 };
    private readonly TextBlock _note = Ui.Label("", 12, Ui.Tertiary, maxLines: 3);
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private bool _busy;

    public PhoneGuide(PhoneService phone, Action test)
    {
        _phone = phone;
        var root = new StackPanel { Spacing = 14, Padding = new Thickness(0, 0, 16, 0) };
        root.Children.Add(Step(1, "Programa en la PC", (PhoneService.CliPath() is not null) ? "KDE Connect ya está instalado." : "Instala KDE Connect (gratis). Tarda un minuto.",
            (PhoneService.CliPath() is not null)
                ? Ui.Primary("Abrir KDE Connect", () => { if (PhoneService.AppPath() is { } a) Process.Start(new ProcessStartInfo(a) { UseShellExecute = true }); }, "\uE8EA")
                : Ui.Primary("Instalar en la PC", () => Process.Start(new ProcessStartInfo("winget", "install --id KDE.KDEConnect -e --accept-source-agreements --accept-package-agreements") { UseShellExecute = true }), "\uE896")));
        var qr = new Image { Width = 150, Height = 150, Source = Qr(PlayStore), HorizontalAlignment = HorizontalAlignment.Left };
        var qrBox = new Border { Background = Ui.Brush(Colors.White), CornerRadius = new CornerRadius(12), Padding = new Thickness(8), Child = qr, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(Step(2, "App en el celular", "Escanea con la cámara e instala «KDE Connect». En iPhone búscala en la App Store.", qrBox));
        var three = new StackPanel { Spacing = 8 };
        three.Children.Add(_devices);
        three.Children.Add(_note);
        root.Children.Add(Step(3, "Vincular", "Misma red Wi-Fi en los dos. Abre la app en el celular; tu teléfono aparece aquí abajo: toca «Vincular» y acepta en el celular.", three));
        root.Children.Add(Step(4, "Permisos", "En el celular, dentro de KDE Connect › tu PC, activa Portapapeles, Compartir y Notificaciones.", Ui.Primary("Probar conexión", test, "\uE73E")));
        Children.Add(new ScrollViewer { Content = root });
        _timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(4);
        _timer.Tick += (_, _) => _ = RefreshAsync();
        Loaded += (_, _) => { _ = RefreshAsync(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    private static BitmapImage Qr(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        var bmp = new BitmapImage();
        using var ms = new MemoryStream(png);
        bmp.SetSource(ms.AsRandomAccessStream());
        return bmp;
    }

    private static UIElement Step(int n, string title, string text, UIElement action)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var num = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Ui.Brush(Ui.Parse("#60A5FA", 0x40)), VerticalAlignment = VerticalAlignment.Top, Child = Ui.Label(n.ToString(), 13, Colors.White, bold: true) };
        ((TextBlock)num.Child).HorizontalAlignment = HorizontalAlignment.Center;
        ((TextBlock)num.Child).VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(num);
        var s = new StackPanel { Spacing = 6 };
        s.Children.Add(Ui.Label(title, 14.5, Ui.Text, bold: true));
        s.Children.Add(Ui.Label(text, 12.5, Ui.Secondary, maxLines: 5));
        s.Children.Add(action);
        Grid.SetColumn(s, 1);
        g.Children.Add(s);
        return g;
    }

    private async Task RefreshAsync()
    {
        if (_busy || !(PhoneService.CliPath() is not null)) return;
        _busy = true;
        try
        {
            var list = await PhoneService.AllAsync();
            _devices.Children.Clear();
            if (list.Count == 0) _note.Text = "Buscando celulares en tu red… abre KDE Connect en el teléfono.";
            else _note.Text = "";
            foreach (var d in list)
            {
                var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(10, 8, 10, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(Ui.Icon("\uE8EA", 18, Colors.White));
                var info = new StackPanel();
                info.Children.Add(Ui.Label(d.Name, 13, Ui.Text, bold: true));
                info.Children.Add(Ui.Label(d.Paired ? (d.Reachable ? "Vinculado ✓" : "Vinculado · fuera de la red") : "Listo para vincular", 11.5, d.Paired ? Ui.Parse("#34D399") : Ui.Tertiary));
                Grid.SetColumn(info, 1);
                row.Children.Add(info);
                if (!d.Paired && d.Reachable)
                {
                    var id = d.Id;
                    var pair = Ui.Primary("Vincular", () => _ = PairAsync(id), "\uE71B");
                    Grid.SetColumn(pair, 2);
                    row.Children.Add(pair);
                }
                _devices.Children.Add(new Border { Child = row, CornerRadius = new CornerRadius(12), Background = Ui.Brush(ColorHelper.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)) });
            }
        }
        catch (PhoneException e)
        {
            _note.Text = e.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task PairAsync(string id)
    {
        _note.Text = "Acepta la solicitud en tu celular…";
        try
        {
            await PhoneService.PairAsync(id);
        }
        catch (PhoneException e)
        {
            _note.Text = e.Message;
        }
    }
}
