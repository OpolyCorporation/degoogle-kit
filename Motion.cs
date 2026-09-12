using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DeGoogleKit;

internal static class Motion
{
    private static IEasingFunction Out => new CubicEase { EasingMode = EasingMode.EaseOut };
    private static IEasingFunction Back => new BackEase { Amplitude = 0.34, EasingMode = EasingMode.EaseOut };

    public static void FadeIn(UIElement el, double from = 0, int ms = 280)
    {
        el.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Out });
    }

    public static void EnterPage(FrameworkElement el)
    {
        el.Opacity = 0;
        var tx = new TranslateTransform(18, 0);
        el.RenderTransform = tx;
        FadeIn(el, 0, 220);
        tx.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = Out });
    }

    public static void EnterRow(FrameworkElement el)
    {
        if (!el.IsVisible) return;
        el.Opacity = 0;
        var tx = new TranslateTransform(0, 10);
        el.RenderTransform = tx;
        FadeIn(el, 0, 200);
        tx.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = Out });
    }

    public static void Pop(FrameworkElement el)
    {
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(0.86, 0.86);
        el.RenderTransform = scale;
        var anim = new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(360)) { EasingFunction = Back };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim.Clone());
    }

    public static void Pulse(UIElement el, bool on)
    {
        if (!on)
        {
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = 1;
            return;
        }

        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.46, 1, TimeSpan.FromMilliseconds(620))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        });
    }

    public static void ProgressTo(ProgressBar bar, double value)
    {
        var from = double.IsNaN(bar.Value) ? 0 : bar.Value;
        bar.BeginAnimation(RangeBase.ValueProperty,
            new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(560)) { EasingFunction = Out });
    }

    public static void ScanBusy(FrameworkElement ribbon, bool on)
    {
        ribbon.RenderTransformOrigin = new Point(0, 0.5);
        if (ribbon.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(0, 1);
            ribbon.RenderTransform = scale;
        }

        if (!on)
        {
            ribbon.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(ribbon.Opacity, 0, TimeSpan.FromMilliseconds(180)));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale.ScaleX, 0, TimeSpan.FromMilliseconds(180)));
            return;
        }

        ribbon.Opacity = 1;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.12, 1, TimeSpan.FromMilliseconds(900))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = Out
        });
    }

    public static void CountPercent(TextBlock tb, int to)
    {
        var from = 0;
        var raw = tb.Text.Trim().TrimEnd('%');
        if (int.TryParse(raw, out var parsed)) from = parsed;

        if (tb.Tag is DispatcherTimer old)
            old.Stop();

        var start = DateTime.UtcNow;
        const double dur = 520;
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        tb.Tag = clock;
        clock.Tick += (_, _) =>
        {
            var t = Math.Min(1, (DateTime.UtcNow - start).TotalMilliseconds / dur);
            var eased = 1 - Math.Pow(1 - t, 3);
            tb.Text = $"{(int)Math.Round(from + (to - from) * eased)}%";
            if (t >= 1)
            {
                clock.Stop();
                tb.Text = $"{to}%";
            }
        };
        clock.Start();
    }

    public static async Task StaggerPop(params FrameworkElement?[] els)
    {
        foreach (var el in els)
        {
            if (el is null) continue;
            Pop(el);
            await Task.Delay(70);
        }
    }
}
