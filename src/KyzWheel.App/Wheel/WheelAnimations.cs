using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace KyzWheel.App.Wheel;

public static class WheelAnimations
{
    private static readonly bool Enabled = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    private static CompositionEasingFunction Ease(Compositor c)
        => c.CreateCubicBezierEasingFunction(new Vector2(0.23f, 1f), new Vector2(0.32f, 1f));

    private static Visual V(UIElement e) => ElementCompositionPreview.GetElementVisual(e);

    private static void Fade(Visual v, float from, float to, int ms, int delayMs = 0)
    {
        var c = v.Compositor;
        var a = c.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(0f, from);
        a.InsertKeyFrame(1f, to, Ease(c));
        a.Duration = TimeSpan.FromMilliseconds(ms);
        a.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        a.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        v.StartAnimation("Opacity", a);
    }

    private static void Scale(Visual v, float from, float to, int ms, int delayMs = 0, CompositionEasingFunction? ease = null)
    {
        var c = v.Compositor;
        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0f, new Vector3(from, from, 1f));
        a.InsertKeyFrame(1f, new Vector3(to, to, 1f), ease ?? Ease(c));
        a.Duration = TimeSpan.FromMilliseconds(ms);
        a.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        a.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        v.StartAnimation("Scale", a);
    }

    public static void SetCenter(UIElement e, float x, float y) => V(e).CenterPoint = new Vector3(x, y, 0);

    public static void Open(UIElement root, IReadOnlyList<UIElement> slices)
    {
        var v = V(root);
        if (!Enabled)
        {
            v.Scale = Vector3.One;
            Fade(v, 0, 1, 80);
            return;
        }
        Scale(v, 0.94f, 1f, 150);
        Fade(v, 0, 1, 120);
        for (var i = 0; i < slices.Count; i++)
        {
            var s = V(slices[i]);
            Fade(s, 0, 1, 140, 12 * i);
            Scale(s, 0.97f, 1f, 160, 12 * i);
        }
    }

    public static void Close(UIElement root, Action done)
    {
        var v = V(root);
        var c = v.Compositor;
        var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
        Fade(v, v.Opacity, 0, Enabled ? 110 : 80);
        if (Enabled) Scale(v, v.Scale.X, 0.96f, 110);
        batch.End();
        batch.Completed += (_, _) => done();
    }

    public static void Hover(UIElement slice, UIElement halo, bool on)
    {
        var ms = Enabled ? 110 : 1;
        Scale(V(slice), V(slice).Scale.X == 0 ? 1 : V(slice).Scale.X, on ? 1.035f : 1f, ms);
        Fade(V(halo), V(halo).Opacity, on ? 0.6f : 0f, ms);
    }

    public static void PageSwitch(UIElement layer, int direction)
    {
        var v = V(layer);
        if (!Enabled)
        {
            Fade(v, 0, 1, 80);
            return;
        }
        var c = v.Compositor;
        var r = c.CreateScalarKeyFrameAnimation();
        r.InsertKeyFrame(0f, 10f * direction);
        r.InsertKeyFrame(1f, 0f, Ease(c));
        r.Duration = TimeSpan.FromMilliseconds(180);
        v.StartAnimation("RotationAngleInDegrees", r);
        Fade(v, 0, 1, 180);
    }

    public static void PanelIn(UIElement panel, IReadOnlyList<UIElement> rows)
    {
        var v = V(panel);
        var c = v.Compositor;
        if (Enabled)
        {
            var t = c.CreateVector3KeyFrameAnimation();
            t.InsertKeyFrame(0f, new Vector3(14, 0, 0));
            t.InsertKeyFrame(1f, Vector3.Zero, Ease(c));
            t.Duration = TimeSpan.FromMilliseconds(200);
            v.StartAnimation("Translation", t);
        }
        Fade(v, 0, 1, Enabled ? 200 : 80);
        if (!Enabled) return;
        for (var i = 0; i < rows.Count; i++) Fade(V(rows[i]), 0, 1, 200, 18 * i);
    }

    public static void BadgePop(UIElement badge)
    {
        var v = V(badge);
        var c = v.Compositor;
        if (!Enabled)
        {
            Fade(v, 0, 1, 80);
            return;
        }
        var spring = c.CreateSpringVector3Animation();
        spring.InitialValue = new Vector3(0.6f, 0.6f, 1f);
        spring.FinalValue = Vector3.One;
        spring.DampingRatio = 0.55f;
        spring.Period = TimeSpan.FromMilliseconds(45);
        v.StartAnimation("Scale", spring);
    }

    public static void Success(UIElement slice)
    {
        if (!Enabled) return;
        var v = V(slice);
        var c = v.Compositor;
        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0f, Vector3.One);
        a.InsertKeyFrame(0.45f, new Vector3(1.07f, 1.07f, 1f), Ease(c));
        a.InsertKeyFrame(1f, Vector3.One, Ease(c));
        a.Duration = TimeSpan.FromMilliseconds(220);
        v.StartAnimation("Scale", a);
    }
}
