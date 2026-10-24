using KyzWheel.Core.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace KyzWheel.App.Settings;

public sealed class HotkeyBox : Button
{
    private bool _recording;
    private string? _value;

    public bool AllowClear { get; init; }

    public event Action<bool>? RecordingChanged;
    public event Action<string?>? Changed;

    public string? Value
    {
        get => _value;
        set
        {
            _value = value;
            Content = string.IsNullOrEmpty(value) ? "Ninguno" : value;
        }
    }

    public HotkeyBox()
    {
        MinWidth = 180;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        Content = "Ninguno";
        Click += (_, _) => Start();
        LostFocus += (_, _) => Stop();
        PreviewKeyDown += OnKey;
        PreviewKeyUp += (_, e) =>
        {
            if (!_recording) return;
            var now = Mods();
            if (now.Length == 0 && _peak.Split('+', StringSplitOptions.RemoveEmptyEntries).Length >= 3)
            {
                var peak = _peak;
                _peak = "";
                if (Hotkey.TryParse(peak, out var h))
                {
                    _value = h.ToString();
                    Changed?.Invoke(_value);
                }
                Stop();
            }
        };
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointer), true);
    }

    private void Start()
    {
        if (_recording) return;
        _recording = true;
        Content = "Presiona la combinación…";
        RecordingChanged?.Invoke(true);
    }

    private void Stop()
    {
        if (!_recording) return;
        _recording = false;
        Value = _value;
        RecordingChanged?.Invoke(false);
    }

    private string _peak = "";

    private static bool Down(VirtualKey k) => InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(CoreVirtualKeyStates.Down);

    private static string Mods()
    {
        var parts = new List<string>();
        if (Down(VirtualKey.Control)) parts.Add("Ctrl");
        if (Down(VirtualKey.Menu)) parts.Add("Alt");
        if (Down(VirtualKey.Shift)) parts.Add("Shift");
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) parts.Add("Win");
        return string.Join('+', parts);
    }

    private void Commit(string key)
    {
        var mods = Mods();
        var text = mods.Length > 0 ? mods + "+" + key : key;
        if (Hotkey.TryParse(text, out var h))
        {
            _value = h.ToString();
            Changed?.Invoke(_value);
        }
        Stop();
    }

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;
        var k = e.Key;
        if (k is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or VirtualKey.LeftWindows or VirtualKey.RightWindows
            or VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.LeftShift or VirtualKey.RightShift or VirtualKey.LeftMenu or VirtualKey.RightMenu)
        {
            var m = Mods();
            if (m.Split('+', StringSplitOptions.RemoveEmptyEntries).Length > _peak.Split('+', StringSplitOptions.RemoveEmptyEntries).Length) _peak = m;
            return;
        }
        _peak = "";
        if (k == VirtualKey.Escape && Mods().Length == 0)
        {
            Stop();
            return;
        }
        if (k is VirtualKey.Back or VirtualKey.Delete && Mods().Length == 0 && AllowClear)
        {
            _value = null;
            Changed?.Invoke(null);
            Stop();
            return;
        }
        string? name = k switch
        {
            >= VirtualKey.A and <= VirtualKey.Z => ((char)k).ToString(),
            >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((char)k).ToString(),
            >= VirtualKey.F1 and <= VirtualKey.F24 => "F" + (k - VirtualKey.F1 + 1),
            VirtualKey.Space => "Space",
            VirtualKey.Tab => "Tab",
            VirtualKey.Enter => "Enter",
            VirtualKey.Escape => "Esc",
            VirtualKey.Insert => "Insert",
            VirtualKey.Delete => "Delete",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            VirtualKey.PageUp => "PageUp",
            VirtualKey.PageDown => "PageDown",
            VirtualKey.Left => "Left",
            VirtualKey.Right => "Right",
            VirtualKey.Up => "Up",
            VirtualKey.Down => "Down",
            _ => null,
        };
        if (name is not null) Commit(name);
    }

    private void OnPointer(object sender, PointerRoutedEventArgs e)
    {
        if (!_recording) return;
        var p = e.GetCurrentPoint(this).Properties;
        var name = p.IsXButton1Pressed ? "Mouse4" : p.IsXButton2Pressed ? "Mouse5" : p.IsMiddleButtonPressed ? "MouseMiddle" : null;
        if (name is null) return;
        e.Handled = true;
        Commit(name);
    }
}
