using KyzWheel.Core.Theming;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace KyzWheel.App.Shell;

public sealed class GlassBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _config;
    private readonly float _strength;

    public GlassBackdrop(float strength = 1f)
    {
        _strength = strength;
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        _config = new SystemBackdropConfiguration { IsInputActive = true, Theme = SystemBackdropTheme.Dark, IsHighContrast = false };
        _controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Base };
        Paint();
        _controller.AddSystemBackdropTarget(target);
        _controller.SetSystemBackdropConfiguration(_config);
        Theme.Changed += Paint;
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        Theme.Changed -= Paint;
        _controller?.RemoveSystemBackdropTarget(target);
        _controller?.Dispose();
        _controller = null;
    }

    private void Paint()
    {
        if (_controller is null) return;
        var t = Theme.Current;
        var tint = Theme.Argb(t.PanelTint);
        _controller.TintColor = Microsoft.UI.ColorHelper.FromArgb(0xFF, tint.R, tint.G, tint.B);
        _controller.TintOpacity = Math.Clamp(tint.A / 255f * _strength, 0f, 1f);
        _controller.LuminosityOpacity = 0.18f;
        _controller.FallbackColor = Microsoft.UI.ColorHelper.FromArgb(0xFF, tint.R, tint.G, tint.B);
    }
}
