using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using WinUIEx;

namespace KyzWheel.App.Wheel;

public sealed partial class WheelWindow : Window
{
    private const int Size = 400;

    public WheelWindow()
    {
        InitializeComponent();
        SystemBackdrop = new TransparentTintBackdrop();
        var p = (OverlappedPresenter)AppWindow.Presenter;
        p.SetBorderAndTitleBar(false, false);
        p.IsResizable = false;
        p.IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = false;
        KyzWheel.Platform.Native.Dwm.MakePopup(this.GetWindowHandle());
        Root.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) Close(); };
    }

    public void ShowAtCursor()
    {
        var pt = KyzWheel.Platform.Native.User32.CursorPosition();
        var at = Environment.GetCommandLineArgs().SkipWhile(a => a != "--at").Skip(1).FirstOrDefault()?.Split(',');
        if (at is { Length: 2 }) pt = (int.Parse(at[0]), int.Parse(at[1]));
        var scale = this.GetDpiForWindow() / 96.0;
        int s = (int)(Size * scale);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(pt.X - s / 2, pt.Y - s / 2, s, s));
        AppWindow.Show();
        Activate();
        AnimateOpen();
    }

    private void AnimateOpen()
    {
        var visual = ElementCompositionPreview.GetElementVisual(Root);
        var c = visual.Compositor;
        visual.CenterPoint = new Vector3(Size / 2f, Size / 2f, 0);
        var ease = c.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
        var scale = c.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, new Vector3(0.88f, 0.88f, 1f));
        scale.InsertKeyFrame(1f, Vector3.One, ease);
        scale.Duration = TimeSpan.FromMilliseconds(170);
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, ease);
        fade.Duration = TimeSpan.FromMilliseconds(170);
        visual.StartAnimation("Scale", scale);
        visual.StartAnimation("Opacity", fade);
    }
}
