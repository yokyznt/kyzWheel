using KyzWheel.App.Shell;
using KyzWheel.Core.Config;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace KyzWheel.App.Settings;

public sealed class SettingsWindow : GlassWindow
{
    private sealed class Draft
    {
        public string Id = Guid.NewGuid().ToString("N")[..8];
        public string Name = "";
        public string Glyph = "\uE7C8";
        public string Color = "#FF8A3D";
        public SlotType Type;
        public string? Hotkey;
        public string? Target;
        public bool Notify;

        public static Draft From(Slot s) => new() { Id = s.Id, Name = s.Name, Glyph = s.Glyph, Color = s.ColorHex, Type = s.Type, Hotkey = s.Hotkey, Target = s.Target, Notify = s.Notify };
        public Slot ToSlot() => new(Id, Name.Trim(), Glyph, Color, Type, string.IsNullOrWhiteSpace(Hotkey) ? null : Hotkey, string.IsNullOrWhiteSpace(Target) ? null : Target.Trim(), Notify);
    }

    public static readonly (string Name, string Glyph)[] Glyphs =
    [
        ("Chat", "\uE8BD"), ("Correo", "\uE715"), ("Convertir", "\uE8AB"), ("Carpeta", "\uE8B7"),
        ("Web", "\uE774"), ("App", "\uE7AC"), ("Estrella", "\uE734"), ("Música", "\uE8D6"),
        ("Foto", "\uEB9F"), ("Código", "\uE943"), ("Juego", "\uE7FC"), ("Nota", "\uE70B"),
    ];

    public static readonly (string Name, string Hex)[] Colors =
    [
        ("Naranja", "#FF8A3D"), ("Verde", "#25D366"), ("Rojo", "#EA4335"), ("Azul", "#38BDF8"),
        ("Morado", "#A78BFA"), ("Rosa", "#F472B6"), ("Amarillo", "#FACC15"), ("Blanco", "#E5E7EB"),
    ];

    private static readonly (SlotType Type, string Name)[] Types =
    [
        (SlotType.WhatsApp, "WhatsApp"), (SlotType.Mail, "Correo"), (SlotType.Converter, "Convertidor"),
        (SlotType.LaunchApp, "Abrir app"), (SlotType.OpenUrl, "Abrir URL"), (SlotType.SendToFolder, "Enviar a carpeta"), (SlotType.Clipboard, "Portapapeles"),
    ];

    private readonly Func<AppConfig, IReadOnlyList<string>> _apply;
    private readonly List<MailAccount> _accounts;
    private string _themeId;
    private readonly GridView _themes = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
    private readonly StackPanel _accountList = new() { Spacing = 6 };

    public Action<IReadOnlyList<MailAccount>, Action<MailAccount>>? AddAccountRequested { get; set; }
    public Action<MailAccount>? AccountRemoved { get; set; }
    private readonly List<Draft> _slots;
    private readonly HotkeyBox _global = new();
    private readonly ListView _list = new() { CanReorderItems = true, AllowDrop = true, SelectionMode = ListViewSelectionMode.Single, Height = 240 };
    private readonly Button _add = new() { Content = "Agregar" };
    private readonly Button _remove = new() { Content = "Quitar" };
    private readonly TextBox _name = new() { Header = "Nombre" };
    private readonly ComboBox _type = new() { Header = "Tipo", MinWidth = 200 };
    private readonly ComboBox _glyph = new() { Header = "Ícono", MinWidth = 140 };
    private readonly ComboBox _color = new() { Header = "Color", MinWidth = 140 };
    private readonly TextBox _target = new() { Header = "Destino" };
    private readonly Button _browse = new() { Content = "Examinar", VerticalAlignment = VerticalAlignment.Bottom };
    private readonly HotkeyBox _slotKey = new() { AllowClear = true };
    private readonly ToggleSwitch _notify = new() { Header = "Notificaciones de Windows" };
    private readonly ToggleSwitch _autostart = new() { Header = "Iniciar con Windows y mantener activo" };
    private readonly ComboBox _accent = new() { Header = "Color de acento", MinWidth = 160 };
    private readonly TextBlock _status;
    private readonly StackPanel _editor = new() { Spacing = 10 };
    private bool _loading;

    public event Action<bool>? RecordingChanged;

    public SettingsWindow(AppConfig config, Func<AppConfig, IReadOnlyList<string>> apply) : base("kyzWheel · Ajustes", 620, 820)
    {
        _apply = apply;
        _slots = config.Slots.Select(Draft.From).ToList();
        _accounts = (config.MailAccounts ?? []).ToList();
        _themeId = config.ThemeId;
        foreach (var t in KyzWheel.Core.Theming.Themes.All) _themes.Items.Add(ThemeCard(t));
        _themes.SelectedIndex = Math.Max(0, KyzWheel.Core.Theming.Themes.All.ToList().FindIndex(t => t.Id == _themeId));
        _themes.SelectionChanged += (_, _) =>
        {
            if (_themes.SelectedItem is FrameworkElement { Tag: string id })
            {
                _themeId = id;
                KyzWheel.App.Shell.Theme.Apply(id);
            }
        };
        _status = Muted("");
        _global.Value = config.GlobalHotkey;
        _global.RecordingChanged += r => RecordingChanged?.Invoke(r);
        _slotKey.RecordingChanged += r => RecordingChanged?.Invoke(r);
        _autostart.IsOn = config.StartWithWindows;
        foreach (var t in Types) _type.Items.Add(new ComboBoxItem { Content = t.Name, Tag = t.Type });
        foreach (var g in Glyphs) _glyph.Items.Add(new ComboBoxItem { Content = Row(g.Glyph, g.Name), Tag = g.Glyph });
        foreach (var c in Colors) _color.Items.Add(new ComboBoxItem { Content = Swatch(c.Hex, c.Name), Tag = c.Hex });
        foreach (var c in Colors) _accent.Items.Add(new ComboBoxItem { Content = Swatch(c.Hex, c.Name), Tag = c.Hex });
        _accent.SelectedItem = _accent.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == config.AccentHex) ?? _accent.Items[0];

        var addMenu = new MenuFlyout();
        foreach (var t in Types)
        {
            var mi = new MenuFlyoutItem { Text = t.Name };
            mi.Click += (_, _) => AddSlot(t.Type, t.Name);
            addMenu.Items.Add(mi);
        }
        _add.Flyout = addMenu;
        _remove.Click += (_, _) => RemoveSelected();
        _list.SelectionChanged += (_, _) => LoadEditor();
        _list.DragItemsCompleted += (_, _) => SyncOrder();
        _name.TextChanged += (_, _) => Edit(d => d.Name = _name.Text, refresh: true);
        _type.SelectionChanged += (_, _) => Edit(d => d.Type = (SlotType)((ComboBoxItem)_type.SelectedItem).Tag, refresh: true);
        _glyph.SelectionChanged += (_, _) => Edit(d => d.Glyph = (string)((ComboBoxItem)_glyph.SelectedItem).Tag, refresh: true);
        _color.SelectionChanged += (_, _) => Edit(d => d.Color = (string)((ComboBoxItem)_color.SelectedItem).Tag, refresh: true);
        _target.TextChanged += (_, _) => Edit(d => d.Target = _target.Text);
        _slotKey.Changed += v => Edit(d => d.Hotkey = v);
        _notify.Toggled += (_, _) => Edit(d => d.Notify = _notify.IsOn);
        _browse.Click += async (_, _) => await BrowseAsync();

        var save = new Button { Content = "Guardar", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        save.Click += (_, _) => Save();

        var root = new StackPanel { Spacing = 16, Padding = new Thickness(24, 44, 24, 24) };
        root.Children.Add(Heading("Ajustes"));
        root.Children.Add(Card(Stack(Heading("Tema", 15), Muted("Cambia el aspecto de la rueda y las ventanas. Se ve al instante."), _themes)));
        root.Children.Add(Card(Stack(Heading("Atajo global", 15), Muted("Abre la rueda donde esté el cursor."), _global)));
        var listButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        listButtons.Children.Add(_add);
        listButtons.Children.Add(_remove);
        var targetRow = new Grid { ColumnSpacing = 8 };
        targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        targetRow.Children.Add(_target);
        Grid.SetColumn(_browse, 1);
        targetRow.Children.Add(_browse);
        var looks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        looks.Children.Add(_glyph);
        looks.Children.Add(_color);
        _editor.Children.Add(_name);
        _editor.Children.Add(_type);
        _editor.Children.Add(looks);
        _editor.Children.Add(targetRow);
        _editor.Children.Add(Stack(Muted("Atajo directo (Retroceso para quitarlo)"), _slotKey));
        _editor.Children.Add(_notify);
        root.Children.Add(Card(Stack(Heading("Secciones", 15), Muted("Entre 1 y 8. Arrastra para ordenar."), _list, listButtons, _editor)));
        var addAccount = new Button { Content = "Agregar cuenta" };
        addAccount.Click += (_, _) => AddAccountRequested?.Invoke(_accounts, a =>
        {
            _accounts.Add(a);
            RenderAccounts();
            Save();
        });
        root.Children.Add(Card(Stack(Heading("Cuentas de correo", 15), Muted("Los correos recientes de todas aparecen juntos en la sección Correo."), _accountList, addAccount)));
        root.Children.Add(Card(Stack(Heading("General", 15), _autostart)));
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        footer.Children.Add(save);
        footer.Children.Add(_status);
        root.Children.Add(footer);
        Surface.Children.Add(new ScrollViewer { Content = root });
        RefreshList(0);
        RenderAccounts();
        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            AppWindow.Hide();
        };
    }

    private static FrameworkElement ThemeCard(KyzWheel.Core.Theming.ThemePalette t)
    {
        var size = 74.0;
        var preview = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
        var glow = KyzWheel.App.Shell.Theme.Argb(t.Glow);
        preview.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = new SolidColorBrush(ColorHelper.FromArgb((byte)(glow.A / 2), glow.R, glow.G, glow.B)), Margin = new Thickness(-4) });
        preview.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = KyzWheel.App.Shell.Theme.Vertical(t.SliceTop, t.SliceBottom), Stroke = KyzWheel.App.Shell.Theme.Edge(t.Rim), StrokeThickness = 1 });
        preview.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = SheenFor(t) });
        preview.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = size * 0.42, Height = size * 0.42, Fill = KyzWheel.App.Shell.Theme.Vertical(t.HubTop, t.HubBottom), Stroke = KyzWheel.App.Shell.Theme.Edge(t.Rim), StrokeThickness = 1 });
        preview.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(KyzWheel.App.Shell.Theme.Argb(t.Accent)) });
        var s = new StackPanel { Spacing = 8, Width = 108, Padding = new Thickness(6, 10, 6, 8), Tag = t.Id };
        s.Children.Add(preview);
        s.Children.Add(new TextBlock { Text = t.Name, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        return s;
    }

    private static Brush SheenFor(KyzWheel.Core.Theming.ThemePalette t)
    {
        var c = KyzWheel.App.Shell.Theme.Argb(t.Sheen);
        var b = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0.2, 0), EndPoint = new Windows.Foundation.Point(0.6, 0.6) };
        b.GradientStops.Add(new GradientStop { Color = c, Offset = 0 });
        b.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0, c.R, c.G, c.B), Offset = 1 });
        return b;
    }

    private void RenderAccounts()
    {
        _accountList.Children.Clear();
        if (_accounts.Count == 0) _accountList.Children.Add(Muted("Aún no hay cuentas."));
        foreach (var a in _accounts)
        {
            var g = new Grid { ColumnSpacing = 10 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(KyzWheel.App.Shell.Ui.Avatar(a.Email, a.ColorHex, 30));
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(new TextBlock { Text = a.Email });
            t.Children.Add(new TextBlock { Text = a.ImapHost, FontSize = 11.5, Opacity = 0.55 });
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            var account = a;
            var remove = KyzWheel.App.Shell.Ui.IconButton("\uE74D", "Quitar cuenta", () =>
            {
                _accounts.Remove(account);
                AccountRemoved?.Invoke(account);
                RenderAccounts();
                Save();
            });
            Grid.SetColumn(remove, 2);
            g.Children.Add(remove);
            _accountList.Children.Add(g);
        }
    }

    private static StackPanel Stack(params UIElement[] children)
    {
        var s = new StackPanel { Spacing = 10 };
        foreach (var c in children) s.Children.Add(c);
        return s;
    }

    private static StackPanel Row(string glyph, string text)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        s.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16 });
        s.Children.Add(new TextBlock { Text = text });
        return s;
    }

    private static StackPanel Swatch(string hex, string text)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        s.Children.Add(new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Parse(hex)) });
        s.Children.Add(new TextBlock { Text = text });
        return s;
    }

    private static Windows.UI.Color Parse(string hex)
    {
        var v = System.Convert.ToUInt32(hex.TrimStart('#'), 16);
        return ColorHelper.FromArgb(0xFF, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    private Draft? Selected => _list.SelectedIndex >= 0 && _list.SelectedIndex < _slots.Count ? _slots[_list.SelectedIndex] : null;

    private void RefreshList(int select)
    {
        _loading = true;
        _list.Items.Clear();
        foreach (var d in _slots)
        {
            var row = Row(d.Glyph, string.IsNullOrWhiteSpace(d.Name) ? "(sin nombre)" : d.Name);
            ((FontIcon)row.Children[0]).Foreground = new SolidColorBrush(Parse(d.Color));
            row.Children.Add(new TextBlock { Text = Types.First(t => t.Type == d.Type).Name, Opacity = 0.55, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            row.Tag = d;
            _list.Items.Add(row);
        }
        _list.SelectedIndex = Math.Clamp(select, -1, _slots.Count - 1);
        _loading = false;
        _add.IsEnabled = _slots.Count < AppConfig.MaxSlots;
        _remove.IsEnabled = _slots.Count > AppConfig.MinSlots;
        LoadEditor();
    }

    private void SyncOrder()
    {
        var order = _list.Items.OfType<StackPanel>().Select(r => (Draft)r.Tag).ToList();
        var selected = Selected;
        _slots.Clear();
        _slots.AddRange(order);
        RefreshList(selected is null ? 0 : _slots.IndexOf(selected));
    }

    private void LoadEditor()
    {
        var d = Selected;
        _editor.Visibility = d is null ? Visibility.Collapsed : Visibility.Visible;
        if (d is null) return;
        _loading = true;
        _name.Text = d.Name;
        _type.SelectedItem = _type.Items.OfType<ComboBoxItem>().First(i => (SlotType)i.Tag == d.Type);
        _glyph.SelectedItem = _glyph.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == d.Glyph);
        _color.SelectedItem = _color.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == d.Color);
        _target.Text = d.Target ?? "";
        _slotKey.Value = d.Hotkey;
        _notify.IsOn = d.Notify;
        UpdateTargetUi(d.Type);
        _loading = false;
    }

    private void UpdateTargetUi(SlotType t)
    {
        var needs = t is SlotType.LaunchApp or SlotType.OpenUrl or SlotType.SendToFolder;
        _target.Visibility = needs ? Visibility.Visible : Visibility.Collapsed;
        _browse.Visibility = t is SlotType.LaunchApp or SlotType.SendToFolder ? Visibility.Visible : Visibility.Collapsed;
        _target.PlaceholderText = t switch
        {
            SlotType.LaunchApp => @"C:\ruta\app.exe",
            SlotType.OpenUrl => "https://…",
            _ => @"C:\ruta\carpeta",
        };
        _notify.Visibility = t is SlotType.WhatsApp or SlotType.Mail ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Edit(Action<Draft> change, bool refresh = false)
    {
        if (_loading || Selected is not { } d) return;
        change(d);
        UpdateTargetUi(d.Type);
        if (!refresh) return;
        var idx = _list.SelectedIndex;
        var row = (StackPanel)_list.Items[idx];
        ((FontIcon)row.Children[0]).Glyph = d.Glyph;
        ((FontIcon)row.Children[0]).Foreground = new SolidColorBrush(Parse(d.Color));
        ((TextBlock)row.Children[1]).Text = string.IsNullOrWhiteSpace(d.Name) ? "(sin nombre)" : d.Name;
        ((TextBlock)row.Children[2]).Text = Types.First(t => t.Type == d.Type).Name;
    }

    private void AddSlot(SlotType type, string name)
    {
        if (_slots.Count >= AppConfig.MaxSlots) return;
        var glyph = type switch
        {
            SlotType.WhatsApp => "\uE8BD",
            SlotType.Mail => "\uE715",
            SlotType.Converter => "\uE8AB",
            SlotType.LaunchApp => "\uE7AC",
            SlotType.Clipboard => "\uE77F",
            SlotType.OpenUrl => "\uE774",
            _ => "\uE8B7",
        };
        _slots.Add(new Draft { Name = name, Type = type, Glyph = glyph, Color = Colors[_slots.Count % Colors.Length].Hex });
        RefreshList(_slots.Count - 1);
    }

    private void RemoveSelected()
    {
        if (_slots.Count <= AppConfig.MinSlots || Selected is not { } d) return;
        var idx = _slots.IndexOf(d);
        _slots.Remove(d);
        RefreshList(Math.Min(idx, _slots.Count - 1));
    }

    private async Task BrowseAsync()
    {
        if (Selected is not { } d) return;
        var hwnd = WindowNative.GetWindowHandle(this);
        if (d.Type == SlotType.LaunchApp)
        {
            var p = new FileOpenPicker();
            InitializeWithWindow.Initialize(p, hwnd);
            p.FileTypeFilter.Add(".exe");
            p.FileTypeFilter.Add(".lnk");
            if (await p.PickSingleFileAsync() is { } f) _target.Text = f.Path;
        }
        else
        {
            var p = new FolderPicker();
            InitializeWithWindow.Initialize(p, hwnd);
            p.FileTypeFilter.Add("*");
            if (await p.PickSingleFolderAsync() is { } f) _target.Text = f.Path;
        }
    }

    private void Save()
    {
        var empty = _slots.FirstOrDefault(s => string.IsNullOrWhiteSpace(s.Name));
        if (empty is not null)
        {
            _status.Text = "Ponle nombre a todas las secciones";
            return;
        }
        var missing = _slots.FirstOrDefault(s => s.Type is SlotType.LaunchApp or SlotType.OpenUrl or SlotType.SendToFolder && string.IsNullOrWhiteSpace(s.Target));
        if (missing is not null)
        {
            _status.Text = $"Falta el destino de {missing.Name}";
            return;
        }
        var cfg = new AppConfig(1, _global.Value ?? "Ctrl+Alt+Space", _slots.Select(s => s.ToSlot()).ToList(),
            KyzWheel.Core.Theming.Themes.Find(_themeId).Accent, _autostart.IsOn) { MailAccounts = _accounts.ToList(), ThemeId = _themeId };
        var errors = ConfigStore.Validate(cfg);
        if (errors.Count == 0) errors = _apply(cfg);
        _status.Text = errors.Count == 0 ? "Guardado" : string.Join(" · ", errors);
    }
}
