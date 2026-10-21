using System;
using System.Globalization;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;
using A = System.Windows.Media.Animation;
using S = System.Windows.Shapes;

namespace MonitorShade
{
    public static class SliderFeedback
    {
        public static readonly W.DependencyProperty EnabledProperty = W.DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(SliderFeedback), new W.PropertyMetadata(false, EnabledChanged));
        public static void SetEnabled(W.DependencyObject target, bool value) { target.SetValue(EnabledProperty, value); }
        public static bool GetEnabled(W.DependencyObject target) { return (bool)target.GetValue(EnabledProperty); }
        private static void EnabledChanged(W.DependencyObject target, W.DependencyPropertyChangedEventArgs e)
        {
            var slider = target as C.Slider;
            if (slider == null || !(bool)e.NewValue) return;
            slider.Loaded += delegate { Update(slider, slider.IsMouseOver); };
            slider.MouseEnter += delegate { Update(slider, true); };
            slider.MouseLeave += delegate { Update(slider, false); };
            slider.GotKeyboardFocus += delegate { Update(slider, slider.IsMouseOver); };
            slider.LostKeyboardFocus += delegate { Update(slider, slider.IsMouseOver); };
        }
        internal static void Update(C.Slider slider, bool hovered)
        {
            var visual = slider.Template.FindName("SliderVisual", slider) as C.Grid;
            var glow = visual == null ? null : visual.Effect as M.Effects.DropShadowEffect;
            if (visual == null || glow == null) return;
            bool active = hovered || slider.IsKeyboardFocusWithin;
            var duration = TimeSpan.FromMilliseconds(W.SystemParameters.ClientAreaAnimation && !W.SystemParameters.HighContrast ? 350 : 0);
            visual.BeginAnimation(W.UIElement.OpacityProperty, new A.DoubleAnimation(active ? 1 : .74, duration));
            glow.BeginAnimation(M.Effects.DropShadowEffect.OpacityProperty, new A.DoubleAnimation(active && !W.SystemParameters.HighContrast ? .55 : 0, duration));
        }
    }

    public sealed class BrightnessGauge : C.Canvas
    {
        public static readonly W.DependencyProperty ValueProperty = W.DependencyProperty.Register("Value", typeof(double), typeof(BrightnessGauge), new W.FrameworkPropertyMetadata(0.0, W.FrameworkPropertyMetadataOptions.AffectsRender, Changed));
        public double Value { get { return (double)GetValue(ValueProperty); } set { SetValue(ValueProperty, value); } }
        private readonly S.Path glow = new S.Path(), progress = new S.Path();
        private readonly S.Ellipse knob = new S.Ellipse { Width = 17, Height = 17, StrokeThickness = 3 };
        public BrightnessGauge()
        {
            IsHitTestVisible = false;
            glow.StrokeThickness = 24; glow.Opacity = .25; glow.Effect = new M.Effects.BlurEffect { Radius = 18 };
            progress.StrokeThickness = 9;
            foreach (S.Path path in new[] { glow, progress }) { path.SetResourceReference(S.Shape.StrokeProperty, "Accent"); path.StrokeStartLineCap = path.StrokeEndLineCap = M.PenLineCap.Round; Children.Add(path); }
            knob.SetResourceReference(S.Shape.FillProperty, "Accent"); knob.SetResourceReference(S.Shape.StrokeProperty, "Input"); Children.Add(knob);
            SizeChanged += delegate { UpdateArc(); };
        }
        private static void Changed(W.DependencyObject d, W.DependencyPropertyChangedEventArgs e) { ((BrightnessGauge)d).UpdateArc(); }
        internal void AnimateTo(double value)
        {
            double from = Value; BeginAnimation(ValueProperty, null); Value = value;
            if (W.SystemParameters.ClientAreaAnimation && IsVisible) BeginAnimation(ValueProperty, new A.DoubleAnimation(from, value, TimeSpan.FromMilliseconds(240)) { EasingFunction = new A.CubicEase { EasingMode = A.EasingMode.EaseOut }, FillBehavior = A.FillBehavior.Stop });
        }
        private double Radius { get { return Math.Max(1, Math.Min(ActualWidth / 2 - 19, ActualHeight - 28)); } }
        private W.Point Point(double fraction, double radius)
        { double angle = Math.PI * (1.065 + .87 * fraction); return new W.Point(ActualWidth / 2 + Math.Cos(angle) * radius, ActualHeight - 4 + Math.Sin(angle) * radius); }
        private M.Geometry Arc(double value)
        {
            var geometry = new M.StreamGeometry();
            using (var c = geometry.Open()) { c.BeginFigure(Point(0, Radius), false, false); c.ArcTo(Point(Math.Max(.00001, value), Radius), new W.Size(Radius, Radius), 0, false, M.SweepDirection.Clockwise, true, false); }
            geometry.Freeze(); return geometry;
        }
        private void UpdateArc()
        {
            double value = Math.Max(0, Math.Min(100, Value)) / 100;
            progress.Data = glow.Data = Arc(value); progress.Visibility = glow.Visibility = value == 0 ? W.Visibility.Hidden : W.Visibility.Visible;
            W.Point p = Point(value, Radius); SetLeft(knob, p.X - 8.5); SetTop(knob, p.Y - 8.5);
        }
        protected override void OnRender(M.DrawingContext dc)
        {
            base.OnRender(dc);
            var track = new M.Pen((M.Brush)FindResource("Soft"), 9) { StartLineCap = M.PenLineCap.Round, EndLineCap = M.PenLineCap.Round };
            dc.DrawGeometry(null, track, Arc(1));
            for (int i = 0; i <= 44; i++)
            {
                var pen = new M.Pen((M.Brush)FindResource(i * 100.0 / 44 <= Value ? "Accent" : "Stroke"), 2) { StartLineCap = M.PenLineCap.Round, EndLineCap = M.PenLineCap.Round };
                dc.DrawLine(pen, Point(i / 44.0, Radius + 10), Point(i / 44.0, Radius + (i % 4 == 0 ? 17 : 15)));
            }
        }
    }

    public sealed class AnimatedPercent : C.TextBlock
    {
        public static readonly W.DependencyProperty ValueProperty = W.DependencyProperty.Register("Value", typeof(double), typeof(AnimatedPercent), new W.PropertyMetadata(0.0, delegate(W.DependencyObject d, W.DependencyPropertyChangedEventArgs e) { ((AnimatedPercent)d).Text = Math.Round((double)e.NewValue).ToString(CultureInfo.InvariantCulture); }));
        public double Value { get { return (double)GetValue(ValueProperty); } set { SetValue(ValueProperty, value); } }
        internal void Clear() { BeginAnimation(ValueProperty, null); Text = "—"; }
        internal void AnimateTo(double value)
        {
            double from = Value; BeginAnimation(ValueProperty, null); Value = value;
            Text = Math.Round(value).ToString(CultureInfo.InvariantCulture);
            if (W.SystemParameters.ClientAreaAnimation && IsVisible) BeginAnimation(ValueProperty, new A.DoubleAnimation(from, value, TimeSpan.FromMilliseconds(240)) { EasingFunction = new A.CubicEase { EasingMode = A.EasingMode.EaseOut }, FillBehavior = A.FillBehavior.Stop });
        }
    }
}
