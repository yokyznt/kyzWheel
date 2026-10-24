using KyzWheel.App.Shell;
using KyzWheel.Core.Config;
using KyzWheel.Core.Files;
using KyzWheel.Mail;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace KyzWheel.App.Mail;

public sealed class ComposeWindow : GlassWindow
{
    private readonly MailService _service;
    private readonly Toasts _toasts;
    private readonly ComboBox _from = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly AutoSuggestBox _to = new() { PlaceholderText = "Para (separa con ;)" };
    private readonly TextBox _subject = new() { PlaceholderText = "Asunto" };
    private readonly TextBox _body = new() { PlaceholderText = "Escribe tu mensaje…", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 200 };
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly List<string> _files = [];
    private readonly TextBlock _status;
    private readonly Button _send;

    public ComposeWindow(MailService service, Toasts toasts, IReadOnlyList<string> files, MailSummary? replyTo = null) : base("kyzWheel · Nuevo correo", 620, 680)
    {
        _service = service;
        _toasts = toasts;
        _status = Ui.Label("", 12, Ui.Tertiary);
        foreach (var a in service.Accounts) _from.Items.Add(new ComboBoxItem { Content = AccountRow(a), Tag = a });
        _from.SelectedIndex = Math.Max(0, service.Accounts.ToList().FindIndex(a => replyTo is not null && a.Email == replyTo.Account));
        _to.TextChanged += (s, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            var last = s.Text.Split(';').Last().Trim();
            s.ItemsSource = last.Length == 0 ? null : service.Inbox.KnownSenders().Where(a => a.Contains(last, StringComparison.OrdinalIgnoreCase)).Take(6).ToList();
        };
        _to.SuggestionChosen += (s, e) =>
        {
            var parts = s.Text.Split(';').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
            parts.Add((string)e.SelectedItem);
            s.Text = string.Join("; ", parts) + "; ";
        };
        if (replyTo is not null)
        {
            _to.Text = replyTo.FromAddress;
            _subject.Text = replyTo.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? replyTo.Subject : "Re: " + replyTo.Subject;
        }
        else if (files.Count > 0)
        {
            _subject.Text = files.Count == 1 ? Path.GetFileNameWithoutExtension(files[0]) : $"{files.Count} archivos";
        }
        AddFiles(files);

        var root = new Grid { Padding = new Thickness(24, 40, 24, 20), RowSpacing = 12, AllowDrop = true };
        for (var i = 0; i < 6; i++) root.RowDefinitions.Add(new RowDefinition { Height = i == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        root.DragOver += (_, e) => e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems) ? DataPackageOperation.Copy : DataPackageOperation.None;
        root.Drop += async (_, e) =>
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
                AddFiles((await e.DataView.GetStorageItemsAsync()).OfType<StorageFile>().Select(f => f.Path).ToList());
        };
        root.Children.Add(Ui.Label(replyTo is null ? "Nuevo correo" : "Responder", 21, bold: true));
        Place(root, Field("De", _from), 1);
        Place(root, Field("Para", _to), 2);
        Place(root, Field("Asunto", _subject), 3);
        Place(root, _body, 4);
        var bottom = new Grid { ColumnSpacing = 10 };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(new ScrollViewer { Content = _chips, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        left.Children.Add(_status);
        bottom.Children.Add(left);
        var attach = Ui.IconButton("\uE723", "Adjuntar", () => _ = PickAsync());
        Grid.SetColumn(attach, 1);
        bottom.Children.Add(attach);
        _send = Ui.Primary("Enviar", () => _ = SendAsync(), "\uE724");
        ToolTipService.SetToolTip(_send, "Ctrl+Enter");
        Grid.SetColumn(_send, 2);
        bottom.Children.Add(_send);
        Place(root, bottom, 5);
        root.KeyDown += (_, e) =>
        {
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (ctrl && e.Key == Windows.System.VirtualKey.Enter) _ = SendAsync();
        };
        Surface.Children.Add(root);
        Activated += (_, _) =>
        {
            if (string.IsNullOrEmpty(_to.Text)) _to.Focus(FocusState.Programmatic);
            else _body.Focus(FocusState.Programmatic);
        };
    }

    private static void Place(Grid g, FrameworkElement e, int row)
    {
        Grid.SetRow(e, row);
        g.Children.Add(e);
    }

    private static Grid Field(string label, FrameworkElement input)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = Ui.Label(label, 12.5, Ui.Tertiary);
        l.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(l);
        Grid.SetColumn(input, 1);
        g.Children.Add(input);
        return g;
    }

    private static StackPanel AccountRow(MailAccount a)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        s.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 8, Height = 8, Fill = Ui.Brush(Ui.Parse(a.ColorHex)), VerticalAlignment = VerticalAlignment.Center });
        s.Children.Add(new TextBlock { Text = a.Email });
        return s;
    }

    private void AddFiles(IReadOnlyList<string> files)
    {
        foreach (var f in files.Where(File.Exists))
            if (!_files.Contains(f, StringComparer.OrdinalIgnoreCase)) _files.Add(f);
        RenderChips();
    }

    private void RenderChips()
    {
        _chips.Children.Clear();
        foreach (var f in _files)
        {
            var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Padding = new Thickness(10, 4, 4, 4) };
            s.Children.Add(Ui.Icon("\uE8A5", 12));
            s.Children.Add(Ui.Label(Path.GetFileName(f), 12));
            s.Children.Add(Ui.Label(BatchSummary.Size(new FileInfo(f).Length), 11, Ui.Tertiary));
            var path = f;
            var x = Ui.IconButton("\uE711", "Quitar", () =>
            {
                _files.Remove(path);
                RenderChips();
            });
            x.Width = 24;
            x.Height = 24;
            s.Children.Add(x);
            _chips.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = Ui.Brush(Microsoft.UI.ColorHelper.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), Child = s });
        }
        var total = _files.Sum(f => new FileInfo(f).Length);
        _status.Text = total > SmtpSender.MaxAttachmentBytes ? $"Adjuntos: {BatchSummary.Size(total)} · el límite es 25 MB" : _files.Count > 0 ? $"Adjuntos: {BatchSummary.Size(total)}" : "";
    }

    private async Task PickAsync()
    {
        var p = new FileOpenPicker();
        InitializeWithWindow.Initialize(p, WindowNative.GetWindowHandle(this));
        p.FileTypeFilter.Add("*");
        AddFiles((await p.PickMultipleFilesAsync()).Select(f => f.Path).ToList());
    }

    private async Task SendAsync()
    {
        if (_from.SelectedItem is not ComboBoxItem { Tag: MailAccount from })
        {
            _status.Text = "Agrega una cuenta en Ajustes";
            return;
        }
        _send.IsEnabled = false;
        _status.Text = "Enviando…";
        try
        {
            await _service.Sender.SendAsync(from, _to.Text, _subject.Text, _body.Text, _files, CancellationToken.None);
            _toasts.Info("Correo enviado", $"Para {_to.Text.Trim().TrimEnd(';')}");
            Close();
        }
        catch (Exception e)
        {
            Log.Error(e);
            _status.Text = e is MailKit.Security.AuthenticationException ? "Revisa la contraseña de aplicación" : e.Message;
            _send.IsEnabled = true;
        }
    }
}
