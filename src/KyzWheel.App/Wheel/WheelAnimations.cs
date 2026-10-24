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

    public static void Hover(UIElement slice, UIElement halo, bool on, double midDeg = double.NaN)
    {
        var ms = Enabled ? 110 : 1;
        var v = V(slice);
        Scale(v, v.Scale.X == 0 ? 1 : v.Scale.X, on ? 1.035f : 1f, ms);
        Fade(V(halo), V(halo).Opacity, on ? 0.6f : 0f, ms);
        if (!Enabled || double.IsNaN(midDeg)) return;
        var a = midDeg * Math.PI / 180;
        var lift = on ? new Vector3((float)(Math.Sin(a) * 5), (float)(-Math.Cos(a) * 5), 0) : Vector3.Zero;
        var c = v.Compositor;
        var spring = c.CreateSpringVector3Animation();
        spring.FinalValue = lift;
        spring.DampingRatio = 1f;
        spring.Period = TimeSpan.FromMilliseconds(38);
        ElementCompositionPreview.SetIsTranslationEnabled(slice, true);
        v.StartAnimation("Translation", spring);
    }

    public static void PointTo(UIElement pointer, double deg, bool visible)
    {
        var v = V(pointer);
        var c = v.Compositor;
        Fade(v, v.Opacity, visible ? 1f : 0f, Enabled ? 120 : 60);
        if (!visible) return;
        var current = v.RotationAngleInDegrees;
        var target = (float)deg;
        while (target - current > 180) target -= 360;
        while (current - target > 180) target += 360;
        if (!Enabled)
        {
            v.RotationAngleInDegrees = target;
            return;
        }
        var spring = c.CreateSpringScalarAnimation();
        spring.FinalValue = target;
        spring.DampingRatio = 1f;
        spring.Period = TimeSpan.FromMilliseconds(32);
        v.StartAnimation("RotationAngleInDegrees", spring);
    }

    public static void Drill(UIElement layer, bool inward)
    {
        var v = V(layer);
        if (layer is FrameworkElement fe && layer is not Microsoft.UI.Xaml.Controls.Canvas) v.CenterPoint = new Vector3((float)fe.ActualWidth / 2, (float)fe.ActualHeight / 2, 0);
        Fade(v, 0, 1, Enabled ? 190 : 80);
        if (!Enabled) return;
        Scale(v, inward ? 0.84f : 1.14f, 1f, 220);
        var c = v.Compositor;
        var r = c.CreateScalarKeyFrameAnimation();
        r.InsertKeyFrame(0f, inward ? -8f : 8f);
        r.InsertKeyFrame(1f, 0f, Ease(c));
        r.Duration = TimeSpan.FromMilliseconds(220);
        if (layer is Microsoft.UI.Xaml.Controls.Canvas) v.StartAnimation("RotationAngleInDegrees", r);
    }

    public static void Crossfade(UIElement root)
    {
        var v = V(root);
        Fade(v, 0.55f, 1f, Enabled ? 200 : 80);
    }

    public static void WindowIn(UIElement root)
    {
        var v = V(root);
        if (root is FrameworkElement fe) v.CenterPoint = new Vector3((float)fe.ActualWidth / 2, (float)fe.ActualHeight / 2, 0);
        Fade(v, 0, 1, Enabled ? 200 : 80);
        if (Enabled) Scale(v, 0.97f, 1f, 240);
    }

    public static void RowEnter(UIElement row)
    {
        var v = V(row);
        Fade(v, 0, 1, Enabled ? 200 : 80);
        if (!Enabled) return;
        var c = v.Compositor;
        ElementCompositionPreview.SetIsTranslationEnabled(row, true);
        var t = c.CreateVector3KeyFrameAnimation();
        t.InsertKeyFrame(0f, new Vector3(0, -10, 0));
        t.InsertKeyFrame(1f, Vector3.Zero, Ease(c));
        t.Duration = TimeSpan.FromMilliseconds(220);
        v.StartAnimation("Translation", t);
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
            ElementCompositionPreview.SetIsTranslationEnabled(panel, true);
            var t = c.CreateVector3KeyFrameAnimation();
            t.InsertKeyFrame(0f, new Vector3(0, 10, 0));
            t.InsertKeyFrame(1f, Vector3.Zero, Ease(c));
            t.Duration = TimeSpan.FromMilliseconds(220);
            v.StartAnimation("Translation", t);
            if (panel is FrameworkElement fe) v.CenterPoint = new Vector3((float)fe.ActualWidth / 2, (float)fe.ActualHeight / 2, 0);
            Scale(v, 0.97f, 1f, 220);
        }
        Fade(v, 0, 1, Enabled ? 200 : 80);
        if (!Enabled) return;
        for (var i = 0; i < Math.Min(rows.Count, 10); i++)
        {
            var r = V(rows[i]);
            Fade(r, 0, 1, 180, 40 + 18 * i);
            ElementCompositionPreview.SetIsTranslationEnabled(rows[i], true);
            var t = c.CreateVector3KeyFrameAnimation();
            t.InsertKeyFrame(0f, new Vector3(0, 6, 0));
            t.InsertKeyFrame(1f, Vector3.Zero, Ease(c));
            t.Duration = TimeSpan.FromMilliseconds(180);
            t.DelayTime = TimeSpan.FromMilliseconds(40 + 18 * i);
            t.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            r.StartAnimation("Translation", t);
        }
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
