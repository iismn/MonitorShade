using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using W = System.Windows;
using M = System.Windows.Media;

namespace MonitorShade
{
    public sealed class AppearancePreferences
    {
        public bool Blur = true;
        public string Theme = "Auto";
        public void Normalize() { if (!new[] { "Auto", "Morning", "Day", "Evening", "Night" }.Contains(Theme)) Theme = "Auto"; }
        internal double Hour(DateTime now)
        { return Theme == "Morning" ? 9 : Theme == "Day" ? 14 : Theme == "Evening" ? 20 : Theme == "Night" ? 0 : now.TimeOfDay.TotalHours; }
    }

    internal sealed class DayPalette
    {
        internal M.Color Background, Panel, Raised, Line, Soft, Accent, Ink, Text, Muted, Aura;
        internal bool Light;
        private static readonly double[] Hours = { 0, 5, 9, 14, 17, 20, 24 };
        private static readonly string[][] Colors = {
            new[] { "18212f","232e3f","2a384a","405168","354962","a5c5ec","bac6d7","24384e" },
            new[] { "39414b","464d56","50555e","74757d","5b6571","d0c6c2","c6c5c2","b48c7c" },
            new[] { "fcf8f2","fffaf4","ffffff","d9ccbf","e5d8c9","ba703e","736456","f7caa4" },
            new[] { "eef0e9","f5f6ef","fafbf6","c8cdbf","d3dac8","64815d","596754","c7d9bd" },
            new[] { "d1c8b6","e2d8c6","ece1ce","afa58f","b5b49a","736746","5a584a","dfb494" },
            new[] { "252723","30332c","383c32","4a5041","414936","c6e4a2","b6bdad","343e2b" },
            new[] { "18212f","232e3f","2a384a","405168","354962","a5c5ec","bac6d7","24384e" }
        };
        internal static M.Color Hex(string value) { return (M.Color)M.ColorConverter.ConvertFromString("#" + value); }
        internal static M.Color Mix(M.Color a, M.Color b, double t)
        { return M.Color.FromArgb((byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t)); }
        internal static M.Color Alpha(M.Color c, byte alpha) { c.A = alpha; return c; }
        private static double Linear(byte v) { double x = v / 255.0; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
        private static double Luminance(M.Color c) { return Linear(c.R) * .2126 + Linear(c.G) * .7152 + Linear(c.B) * .0722; }
        internal static double Contrast(M.Color a, M.Color b) { double x = Luminance(a), y = Luminance(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05); }
        private static M.Color Legible(M.Color color, M.Color[] backgrounds, M.Color target, double ratio)
        { for (int n = 0; n <= 100; n++) { var result = Mix(color, target, n / 100.0); if (backgrounds.Min(b => Contrast(result, b)) >= ratio || n == 100) return result; } return target; }
        internal static DayPalette At(double hour)
        {
            double h = ((hour % 24) + 24) % 24; int i = 0; while (i < Hours.Length - 2 && h > Hours[i + 1]) i++;
            double p = (h - Hours[i]) / (Hours[i + 1] - Hours[i]), t = p * p * (3 - 2 * p);
            M.Color[] c = Enumerable.Range(0, 8).Select(n => Mix(Hex(Colors[i][n]), Hex(Colors[i + 1][n]), t)).ToArray();
            var palette = new DayPalette { Background = c[0], Panel = c[1], Raised = c[2], Line = c[3], Soft = c[4], Accent = c[5], Muted = c[6], Aura = c[7] };
            M.Color dark = Hex("22271f"), light = Hex("fbfaf4"); M.Color[] surfaces = { c[0], c[1] };
            // Around dusk/dawn, light and dark surfaces can straddle the contrast crossover.
            // Narrow their luminance gap before changing foreground, so text never fades to grey.
            if (Math.Max(surfaces.Min(b => Contrast(dark, b)), surfaces.Min(b => Contrast(light, b))) < 4.6)
            {
                dark = M.Colors.Black; light = M.Colors.White;
                for (int step = 1; step <= 100 && Math.Max(surfaces.Min(b => Contrast(dark, b)), surfaces.Min(b => Contrast(light, b))) < 4.5; step++)
                {
                    palette.Panel = Mix(c[1], c[0], step / 100.0);
                    palette.Raised = Mix(c[2], c[0], step / 100.0);
                    surfaces[1] = palette.Panel;
                }
            }
            palette.Light = surfaces.Min(b => Contrast(dark, b)) > surfaces.Min(b => Contrast(light, b));
            palette.Text = palette.Light ? dark : light;
            palette.Muted = Legible(palette.Muted, surfaces, palette.Text, 4.6);
            palette.Accent = Legible(palette.Accent, new[] { palette.Panel }, palette.Text, 3);
            palette.Ink = Contrast(dark, palette.Accent) > Contrast(light, palette.Accent) ? dark : light;
            return palette;
        }
    }

    internal static class DesktopBackdrop
    {
        [StructLayout(LayoutKind.Sequential)] private struct Margins { internal int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
        [DllImport("dwmapi.dll")] private static extern int DwmIsCompositionEnabled(out bool enabled);
        internal static bool Supported { get { return Environment.OSVersion.Version.Build >= 22621; } }
        internal static bool TransparencyEnabled
        {
            get
            {
                if (W.SystemParameters.HighContrast || !Supported) return false;
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))
                        if (key != null && Convert.ToInt32(key.GetValue("EnableTransparency", 1)) == 0) return false;
                    bool enabled; return DwmIsCompositionEnabled(out enabled) == 0 && enabled;
                }
                catch { return false; }
            }
        }
        internal static bool Apply(IntPtr hwnd, bool requested, bool dark)
        {
            if (hwnd == IntPtr.Zero) return false;
            int darkValue = dark ? 1 : 0, rounded = 2;
            if (Environment.OSVersion.Version.Build >= 22000) { DwmSetWindowAttribute(hwnd, 20, ref darkValue, 4); DwmSetWindowAttribute(hwnd, 33, ref rounded, 4); }
            bool enabled = requested && TransparencyEnabled;
            int backdrop = enabled ? 3 : 1;
            if (Supported && DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4) != 0) enabled = false;
            Margins margins = new Margins { Left = enabled ? -1 : 0, Right = enabled ? -1 : 0, Top = enabled ? -1 : 0, Bottom = enabled ? -1 : 0 };
            if (DwmExtendFrameIntoClientArea(hwnd, ref margins) != 0) enabled = false;
            return enabled;
        }
    }
}
