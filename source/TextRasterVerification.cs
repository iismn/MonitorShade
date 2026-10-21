using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;

namespace MonitorShade
{
    // RenderTargetBitmap uses a different (off-screen) text path. This opt-in test
    // checks pixels from our own visible HWND, without touching monitor hardware.
    internal static class TextRasterVerification
    {
        private sealed class ProbeWindow : SettingsWindow { }
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);

        private static int ColoredPixels(W.FrameworkElement text, W.Window window, string imagePath)
        {
            var origin = text.PointToScreen(new W.Point());
            var client = window.PointToScreen(new W.Point());
            var scale = W.PresentationSource.FromVisual(text).CompositionTarget.TransformToDevice;
            int width = (int)Math.Ceiling(text.ActualWidth * scale.M11), height = (int)Math.Ceiling(text.ActualHeight * scale.M22);
            using (var bitmap = new Bitmap(width, height))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    IntPtr source = GetDC(new W.Interop.WindowInteropHelper(window).Handle), destination = graphics.GetHdc();
                    try
                    {
                        if (source == IntPtr.Zero || !BitBlt(destination, 0, 0, width, height, source, (int)Math.Round(origin.X - client.X), (int)Math.Round(origin.Y - client.Y), 0x00CC0020))
                            throw new InvalidOperationException("Could not capture the test window's rendered pixels.");
                    }
                    finally { graphics.ReleaseHdc(destination); if (source != IntPtr.Zero) ReleaseDC(new W.Interop.WindowInteropHelper(window).Handle, source); }
                }
                bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
                int colored = 0, ink = 0;
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    var p = bitmap.GetPixel(x, y);
                    if (p.R < 180 && p.G < 180 && p.B < 180) ink++;
                    if (Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B)) > 12) colored++;
                }
                if (ink < 30) throw new InvalidOperationException("Text capture contained no usable glyphs; the desktop may be unavailable.");
                return colored;
            }
        }

        internal static int Run(string path)
        {
            var results = new List<string>();
            var application = new W.Application { ShutdownMode = W.ShutdownMode.OnExplicitShutdown };
            try
            {
                C.UserControl template;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MonitorShade.Settings.xaml")) template = (C.UserControl)W.Markup.XamlReader.Load(stream);
                var panel = new C.StackPanel { Margin = new W.Thickness(20), Resources = template.Resources };
                panel.SetValue(W.Documents.TextElement.FontFamilyProperty, TextPresentation.DesignFont);
                panel.Resources["Card"] = M.Brushes.White;
                panel.Resources["Text"] = M.Brushes.Black;
                TextPresentation.Apply(panel, true);
                var samples = new List<C.TextBlock>();
                foreach (double size in new[] { 12.0, 14.0, 18.0, 24.0 })
                {
                    var row = new C.StackPanel();
                    foreach (bool clear in new[] { true, false })
                    {
                        var text = new C.TextBlock { Text = "밝기 화면 보호 함께 조절 73%", FontSize = size, Foreground = M.Brushes.Black, HorizontalAlignment = W.HorizontalAlignment.Left };
                        if (!clear) M.TextOptions.SetTextRenderingMode(text, M.TextRenderingMode.Grayscale);
                        row.Children.Add(text); samples.Add(text);
                    }
                    panel.Children.Add(new C.Border { Style = (W.Style)panel.Resources["CardStyle"], Margin = new W.Thickness(0, 0, 0, 8), Child = row });
                }
                using (var window = new ProbeWindow { Title = "MonitorShade text rendering verification", Width = 620, Height = 650, Background = M.Brushes.Transparent, Content = new C.ScrollViewer { Content = panel }, WindowStartupLocation = W.WindowStartupLocation.CenterScreen })
                {
                    window.Show(); window.Activate();
                    bool glass = DesktopBackdrop.Apply(window.Handle, true, true);
                    results.Add("INFO: Native desktop acrylic=" + glass);
                    var frame = new W.Threading.DispatcherFrame();
                    var timer = new W.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                    timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); W.Threading.Dispatcher.PushFrame(frame);
                    for (int i = 0; i < samples.Count; i += 2)
                    {
                        int clear = ColoredPixels(samples[i], window, path + "." + i + "-cleartype.png");
                        int gray = ColoredPixels(samples[i + 1], window, path + "." + i + "-grayscale.png");
                        bool pass = clear > 30 && gray < clear / 5;
                        results.Add((pass ? "PASS: " : "FAIL: ") + "Pretendard " + samples[i].FontSize + " DIP on an opaque card over acrylic: subpixel edges=" + clear + ", grayscale control=" + gray);
                        if (!pass) throw new InvalidOperationException("ClearType pixels could not be distinguished from the grayscale control.");
                    }
                    results.Add("INFO: Effective display DPI=" + (W.PresentationSource.FromVisual(window).CompositionTarget.TransformToDevice.M11 * 96));
                }
                File.WriteAllLines(path, results); return 0;
            }
            catch (Exception e) { results.Add("FAIL: " + e); File.WriteAllLines(path, results); return 1; }
            finally { application.Shutdown(); }
        }
    }
}
