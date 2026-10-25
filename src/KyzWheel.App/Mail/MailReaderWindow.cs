using System.Diagnostics;
using KyzWheel.App.Shell;
using KyzWheel.Core.Html;
using KyzWheel.Mail;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;

namespace KyzWheel.App.Mail;

public sealed class MailReaderWindow : GlassWindow
{
    private readonly MailService _service;
    private readonly MailSummary _mail;
    private readonly WebView2 _web = new() { DefaultBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x0D, 0x0D, 0x12) };
    private readonly StackPanel _attachments = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly TextBlock _note;
    private readonly Button _images;
    private MailBody? _body;
    private bool _remote;

    public MailReaderWindow(MailService service, MailSummary mail, Action<MailSummary> reply) : base("kyzWheel · " + mail.Subject, 760, 760)
    {
        _service = service;
        _mail = mail;
        _note = Ui.Label("", 12, Ui.Tertiary);
        var color = service.AccountOf(mail.Account)?.ColorHex ?? "#FF8A3D";
        var root = new Grid { Padding = new Thickness(24, 40, 24, 20), RowSpacing = 14 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var subject = Ui.Label(mail.Subject, 21, bold: true, maxLines: 2);
        Ui.CopyLine(subject);
        root.Children.Add(subject);

        var meta = new Grid { ColumnSpacing = 12 };
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meta.Children.Add(Ui.Avatar(mail.FromName, color, 40));
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var from = Ui.Label(mail.FromName, 14, bold: true);
        Ui.CopyLine(from);
        who.Children.Add(from);
        var addr = Ui.Label($"{mail.FromAddress} · para {mail.Account} · {mail.Date.ToLocalTime():d MMM yyyy, HH:mm}", 12, Ui.Tertiary);
        Ui.CopyLine(addr);
        who.Children.Add(addr);
        Grid.SetColumn(who, 1);
        meta.Children.Add(who);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        _images = Ui.IconButton("\uEB9F", "Cargar imágenes", LoadImages);
        _images.Visibility = Visibility.Collapsed;
        tools.Children.Add(_images);
        tools.Children.Add(Ui.IconButton("\uE8C8", "Copiar todo el texto", CopyAll));
        tools.Children.Add(Ui.IconButton("\uE97A", "Responder", () => reply(mail)));
        tools.Children.Add(Ui.IconButton("\uE8A7", "Abrir en el navegador", OpenInBrowser));
        Grid.SetColumn(tools, 2);
        meta.Children.Add(tools);
        Grid.SetRow(meta, 1);
        root.Children.Add(meta);

        var frame = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderBrush = Ui.Brush(Ui.Hairline),
            BorderThickness = new Thickness(1),
            Child = _web,
        };
        Grid.SetRow(frame, 2);
        root.Children.Add(frame);

        var footer = new Grid { ColumnSpacing = 10 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(new ScrollViewer { Content = _attachments, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Grid.SetColumn(_note, 1);
        _note.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(_note);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        Surface.Children.Add(root);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(Log.Dir, "webview"));
            await _web.EnsureCoreWebView2Async();
            var s = _web.CoreWebView2.Settings;
            s.IsScriptEnabled = false;
            s.AreDefaultScriptDialogsEnabled = false;
            s.IsWebMessageEnabled = false;
            s.AreDevToolsEnabled = false;
            s.IsStatusBarEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsGeneralAutofillEnabled = false;
            s.IsPasswordAutosaveEnabled = false;
            _web.CoreWebView2.NavigationStarting += OnNavigating;
            _web.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                OpenExternal(e.Uri);
            };
            _web.NavigateToString(MailCsp.WrapText("Cargando…"));
            var src = _service.SourceOf(_mail.Account) ?? throw new InvalidOperationException("La cuenta no está conectada");
            _body = await src.GetBodyAsync(_mail.Uid, CancellationToken.None);
            Render();
            foreach (var a in _body.Attachments) _attachments.Children.Add(AttachmentChip(src, a));
            if (_mail.Unread)
            {
                await src.MarkReadAsync(_mail.Uid, CancellationToken.None);
                _service.Inbox.MarkRead(_mail.Account, _mail.Uid);
            }
        }
        catch (Exception e)
        {
            Log.Error(e);
            _web.NavigateToString(MailCsp.WrapText("No se pudo abrir el correo: " + e.Message));
        }
    }

    private void Render()
    {
        if (_body is null) return;
        if (!string.IsNullOrWhiteSpace(_body.Html))
        {
            var html = _body.Html;
            foreach (var (cid, data) in _body.InlineImages) html = html.Replace("cid:" + cid, data, StringComparison.OrdinalIgnoreCase);
            var hasRemote = html.Contains("src=\"http", StringComparison.OrdinalIgnoreCase) || html.Contains("src='http", StringComparison.OrdinalIgnoreCase) || html.Contains("url(http", StringComparison.OrdinalIgnoreCase);
            _images.Visibility = hasRemote && !_remote ? Visibility.Visible : Visibility.Collapsed;
            _note.Text = hasRemote && !_remote ? "Imágenes remotas bloqueadas" : "";
            _web.NavigateToString(MailCsp.Wrap(html, _remote));
        }
        else
        {
            _web.NavigateToString(MailCsp.WrapText(_body.Text ?? ""));
        }
    }

    private void OnNavigating(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
        e.Cancel = true;
        OpenExternal(e.Uri);
    }

    private static void OpenExternal(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Scheme is "http" or "https" or "mailto"))
            Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
    }

    private void LoadImages()
    {
        _remote = true;
        Render();
    }

    private void CopyAll()
    {
        if (_body is null) return;
        var text = !string.IsNullOrWhiteSpace(_body.Text) ? _body.Text : PreviewText.From(null, _body.Html, int.MaxValue);
        var dp = new DataPackage();
        dp.SetText($"{_mail.Subject}\n{_mail.FromName} <{_mail.FromAddress}>\n\n{text}");
        Clipboard.SetContent(dp);
        _note.Text = "Texto copiado";
    }

    private void OpenInBrowser()
    {
        var url = _mail.Account.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase) || _mail.Account.EndsWith("@googlemail.com", StringComparison.OrdinalIgnoreCase)
            ? $"https://mail.google.com/mail/u/{_mail.Account}/#search/rfc822msgid%3A{Uri.EscapeDataString(_mail.MessageId.Trim('<', '>'))}"
            : _mail.Account.Contains("@outlook.") || _mail.Account.Contains("@hotmail.") || _mail.Account.Contains("@live.")
                ? "https://outlook.live.com/mail/0/inbox"
                : "https://mail.google.com/";
        OpenExternal(url);
    }

    private UIElement AttachmentChip(IMailSource src, MailAttachmentInfo a)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(10, 6, 12, 6) };
        s.Children.Add(Ui.Icon("\uE8A5", 14, Ui.Secondary));
        s.Children.Add(Ui.Label(a.FileName, 12.5));
        if (a.Size > 0) s.Children.Add(Ui.Label(KyzWheel.Core.Files.BatchSummary.Size(a.Size), 11.5, Ui.Tertiary));
        var b = new Border { CornerRadius = new CornerRadius(8), Background = Ui.Brush(Microsoft.UI.ColorHelper.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), Child = s };
        Ui.Pressable(b);
        b.Tapped += async (_, _) =>
        {
            try
            {
                _note.Text = "Descargando…";
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                var path = await src.SaveAttachmentAsync(_mail.Uid, a, folder, CancellationToken.None);
                _note.Text = "Guardado en Descargas";
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch (Exception e)
            {
                Log.Error(e);
                _note.Text = "No se pudo descargar";
            }
        };
        return b;
    }
}
