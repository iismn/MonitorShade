using System;
using System.Collections.Generic;
using System.Linq;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;
using A = System.Windows.Media.Animation;

namespace MonitorShade
{
    internal static class TextPresentation
    {
        internal static readonly M.FontFamily DesignFont = new M.FontFamily(new Uri("pack://application:,,,/"), "./fonts/#Pretendard");
        private static readonly M.FontFamily WindowsFont = new M.FontFamily("Malgun Gothic");
        internal static bool HasWindowsFont
        {
            get
            {
                M.GlyphTypeface glyph;
                return new M.Typeface(WindowsFont, W.FontStyles.Normal, W.FontWeights.Normal, W.FontStretches.Normal).TryGetGlyphTypeface(out glyph)
                    && glyph.FamilyNames.Values.Contains("Malgun Gothic") && glyph.CharacterToGlyphMap.ContainsKey('한');
            }
        }
        internal static M.FontFamily KoreanFamily(string choice, bool available)
        { return choice == "MalgunGothic" && available ? WindowsFont : DesignFont; }
        internal static void Apply(W.DependencyObject root, bool glass)
        {
            M.TextOptions.SetTextFormattingMode(root, M.TextFormattingMode.Display);
            M.TextOptions.SetTextHintingMode(root, M.TextHintingMode.Fixed);
            // Subpixel ClearType cannot correctly blend with an unknown translucent backdrop.
            // Keep grayscale antialiasing on glass; opaque surfaces can use the system's ClearType.
            M.TextOptions.SetTextRenderingMode(root, glass ? M.TextRenderingMode.Grayscale : M.TextRenderingMode.Auto);
            M.RenderOptions.SetClearTypeHint(root, glass ? M.ClearTypeHint.Auto : M.ClearTypeHint.Enabled);
        }
    }

    public static class NavigationFeedback
    {
        public static readonly W.DependencyProperty EnabledProperty = W.DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(NavigationFeedback), new W.PropertyMetadata(false, EnabledChanged));
        public static void SetEnabled(W.DependencyObject target, bool value) { target.SetValue(EnabledProperty, value); }
        public static bool GetEnabled(W.DependencyObject target) { return (bool)target.GetValue(EnabledProperty); }
        private static void EnabledChanged(W.DependencyObject target, W.DependencyPropertyChangedEventArgs e)
        {
            var item = target as C.RadioButton;
            if (item == null || !(bool)e.NewValue) return;
            item.Loaded += delegate { Update(item, item.IsMouseOver, false); };
            item.MouseEnter += delegate { Update(item, true, true); };
            item.MouseLeave += delegate { Update(item, false, true); };
            item.Checked += delegate { Update(item, item.IsMouseOver, true); };
            item.Unchecked += delegate { Update(item, item.IsMouseOver, true); };
            item.GotKeyboardFocus += delegate { Update(item, item.IsMouseOver, true); };
            item.LostKeyboardFocus += delegate { Update(item, item.IsMouseOver, true); };
        }
        internal static void Update(C.RadioButton item, bool hovered, bool animate)
        {
            if (item.Template == null) return;
            var surface = item.Template.FindName("Surface", item) as C.Border;
            var wash = item.Template.FindName("HoverWash", item) as C.Border;
            var indicator = item.Template.FindName("Indicator", item) as C.Border;
            if (surface == null || wash == null || indicator == null) return;
            bool selected = item.IsChecked == true, active = hovered || item.IsKeyboardFocused;
            bool motion = animate && W.SystemParameters.ClientAreaAnimation && !W.SystemParameters.HighContrast;
            var duration = TimeSpan.FromMilliseconds(motion ? 180 : 0);
            surface.BeginAnimation(W.UIElement.OpacityProperty, new A.DoubleAnimation(selected ? 1 : active ? .6 : 0, duration));
            wash.BeginAnimation(W.UIElement.OpacityProperty, new A.DoubleAnimation(active && !W.SystemParameters.HighContrast ? .13 : 0, duration));
            indicator.BeginAnimation(W.UIElement.OpacityProperty, new A.DoubleAnimation(selected ? 1 : 0, duration));
            var scale = (M.ScaleTransform)indicator.RenderTransform;
            if (scale.IsFrozen) { scale = scale.Clone(); indicator.RenderTransform = scale; }
            scale.BeginAnimation(M.ScaleTransform.ScaleYProperty, new A.DoubleAnimation(selected ? (active ? 1.2 : 1) : .5, duration) { EasingFunction = new A.CubicEase { EasingMode = A.EasingMode.EaseOut } });
            var target = (M.SolidColorBrush)item.FindResource(selected || active ? "Text" : "Muted");
            var ink = item.Foreground as M.SolidColorBrush;
            if (ink == null || ink.IsFrozen || item.ReadLocalValue(C.Control.ForegroundProperty) == W.DependencyProperty.UnsetValue)
            { ink = new M.SolidColorBrush(target.Color); item.Foreground = ink; }
            ink.BeginAnimation(M.SolidColorBrush.ColorProperty, new A.ColorAnimation(target.Color, duration));
        }
    }

    internal sealed class PageTransition : IDisposable
    {
        private readonly C.Grid host;
        private readonly IDictionary<string, C.ScrollViewer> pages;
        private string active, requested;
        private int revision;
        private bool disposed;
        internal string Requested { get { return requested; } }
        internal PageTransition(C.Grid host, IDictionary<string, C.ScrollViewer> pages)
        { this.host = host; this.pages = pages; }
        internal void Select(string name, bool animate)
        {
            if (disposed || !pages.ContainsKey(name)) return;
            if (name == requested && active == name && host.IsHitTestVisible) return;
            requested = name;
            int token = ++revision;
            foreach (var page in pages.Values)
            {
                double opacity = page.Opacity;
                page.BeginAnimation(W.UIElement.OpacityProperty, null); page.Opacity = opacity;
            }
            if (active == null || !animate || !host.IsVisible || !W.SystemParameters.ClientAreaAnimation || W.SystemParameters.HighContrast)
            { Finish(); return; }
            host.IsHitTestVisible = false;
            var old = pages[active];
            var fadeOut = new A.DoubleAnimation(old.Opacity, 0, TimeSpan.FromMilliseconds(95)) { FillBehavior = A.FillBehavior.Stop };
            fadeOut.Completed += delegate
            {
                if (disposed || token != revision) return;
                ShowOnly(name); active = name;
                var next = pages[name]; next.Opacity = 1;
                var fadeIn = new A.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(175)) { FillBehavior = A.FillBehavior.Stop, EasingFunction = new A.CubicEase { EasingMode = A.EasingMode.EaseOut } };
                fadeIn.Completed += delegate { if (!disposed && token == revision) { next.BeginAnimation(W.UIElement.OpacityProperty, null); host.IsHitTestVisible = true; } };
                next.BeginAnimation(W.UIElement.OpacityProperty, fadeIn);
            };
            old.BeginAnimation(W.UIElement.OpacityProperty, fadeOut);
        }
        private void ShowOnly(string name)
        {
            foreach (var pair in pages)
            { pair.Value.BeginAnimation(W.UIElement.OpacityProperty, null); pair.Value.Opacity = 1; pair.Value.Visibility = pair.Key == name ? W.Visibility.Visible : W.Visibility.Collapsed; }
        }
        internal void Finish()
        { ++revision; if (requested != null) { ShowOnly(requested); active = requested; } host.IsHitTestVisible = true; }
        public void Dispose() { Finish(); disposed = true; }
    }
}
