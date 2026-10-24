using System.Diagnostics;
using KyzWheel.App.Shell;
using KyzWheel.Core.Config;
using KyzWheel.Mail;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KyzWheel.App.Mail;

public sealed class AddAccountWindow : GlassWindow
{
    private readonly TextBox _email = new() { PlaceholderText = "tu@gmail.com", InputScope = new Microsoft.UI.Xaml.Input.InputScope { Names = { new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.EmailSmtpAddress) } } };
    private readonly PasswordBox _password = new() { PlaceholderText = "xxxx xxxx xxxx xxxx" };
    private readonly TextBox _name = new() { PlaceholderText = "Opcional: nombre que verán al recibir" };
    private readonly HyperlinkButton _help = new() { Content = "Crear contraseña de aplicación", Padding = new Thickness(0) };
    private readonly Expander _advanced = new() { Header = "Servidor (avanzado)", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _imap = new() { Header = "IMAP" };
    private readonly NumberBox _imapPort = new() { Header = "Puerto", Minimum = 1, Maximum = 65535 };
    private readonly TextBox _smtp = new() { Header = "SMTP" };
    private readonly NumberBox _smtpPort = new() { Header = "Puerto", Minimum = 1, Maximum = 65535 };
    private readonly TextBlock _status;
    private readonly Button _save;
    private readonly ProgressRing _ring = new() { IsActive = false, Width = 18, Height = 18 };

    public event Action<MailAccount>? Added;

    public AddAccountWindow(ISecretStore secrets, IReadOnlyList<MailAccount> existing) : base("kyzWheel · Agregar cuenta", 480, 640)
    {
        _status = Ui.Label("", 12.5, Ui.Secondary, maxLines: 3);
        _email.TextChanged += (_, _) => Prefill();
        _help.Click += (_, _) =>
        {
            if (_help.Tag is string url) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        };
        var adv = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        adv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        adv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        adv.RowDefinitions.Add(new RowDefinition());
        adv.RowDefinitions.Add(new RowDefinition());
        adv.Children.Add(_imap);
        Grid.SetColumn(_imapPort, 1);
        adv.Children.Add(_imapPort);
        Grid.SetRow(_smtp, 1);
        adv.Children.Add(_smtp);
        Grid.SetRow(_smtpPort, 1);
        Grid.SetColumn(_smtpPort, 1);
        adv.Children.Add(_smtpPort);
        _advanced.Content = adv;

        _save = Ui.Primary("Probar y guardar", () => _ = SaveAsync(secrets, existing), "\uE73E");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        buttons.Children.Add(_save);
        buttons.Children.Add(_ring);

        var s = new StackPanel { Spacing = 14, Padding = new Thickness(26, 42, 26, 24) };
        s.Children.Add(Ui.Label("Agregar cuenta", 21, bold: true));
        s.Children.Add(Ui.Label("Se conecta directo a tu proveedor por IMAP. La contraseña se guarda en el Administrador de credenciales de Windows, nunca en un archivo.", 12.5, Ui.Secondary, maxLines: 4));
        s.Children.Add(Labeled("Correo", _email));
        var pass = Labeled("Contraseña de aplicación", _password);
        s.Children.Add(pass);
        s.Children.Add(_help);
        s.Children.Add(Labeled("Nombre", _name));
        s.Children.Add(_advanced);
        s.Children.Add(buttons);
        s.Children.Add(_status);
        Surface.Children.Add(new ScrollViewer { Content = s });
        Prefill();
    }

    private static StackPanel Labeled(string label, FrameworkElement input)
    {
        var s = new StackPanel { Spacing = 6 };
        s.Children.Add(Ui.Label(label, 12.5, Ui.Secondary));
        s.Children.Add(input);
        return s;
    }

    private void Prefill()
    {
        try
        {
            var p = ProviderPresets.For(_email.Text);
            _imap.Text = p.ImapHost;
            _imapPort.Value = p.ImapPort;
            _smtp.Text = p.SmtpHost;
            _smtpPort.Value = p.SmtpPort;
            var help = ProviderPresets.AppPasswordHelp(_email.Text);
            _help.Tag = help;
            _help.Visibility = help is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (ArgumentException)
        {
            _help.Visibility = Visibility.Collapsed;
        }
    }

    private async Task SaveAsync(ISecretStore secrets, IReadOnlyList<MailAccount> existing)
    {
        MailAccount account;
        try
        {
            var p = ProviderPresets.For(_email.Text);
            account = p with
            {
                DisplayName = string.IsNullOrWhiteSpace(_name.Text) ? p.Email : _name.Text.Trim(),
                ImapHost = _imap.Text.Trim(),
                ImapPort = (int)_imapPort.Value,
                SmtpHost = _smtp.Text.Trim(),
                SmtpPort = (int)_smtpPort.Value,
                SmtpSecurity = (int)_smtpPort.Value == 587 ? SmtpSecurity.StartTls : SmtpSecurity.SslOnConnect,
            };
        }
        catch (ArgumentException)
        {
            _status.Text = "Escribe un correo válido";
            return;
        }
        if (existing.Any(a => a.Email == account.Email))
        {
            _status.Text = "Esa cuenta ya está agregada";
            return;
        }
        var password = _password.Password.Replace(" ", "");
        if (password.Length == 0)
        {
            _status.Text = "Falta la contraseña de aplicación";
            return;
        }
        _save.IsEnabled = false;
        _ring.IsActive = true;
        _status.Text = "Probando conexión…";
        var error = await ImapMailSource.TestAsync(account, password, CancellationToken.None);
        _ring.IsActive = false;
        _save.IsEnabled = true;
        if (error is not null)
        {
            _status.Text = error;
            return;
        }
        secrets.Set(ProviderPresets.SecretKey(account.Email), password);
        _password.Password = "";
        Added?.Invoke(account);
        Close();
    }
}
