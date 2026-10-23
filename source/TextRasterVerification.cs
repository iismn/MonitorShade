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
        private static void Wait(int ms)
        {
            var frame = new W.Threading.DispatcherFrame();
            var timer = new W.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); W.Threading.Dispatcher.PushFrame(frame);
        }
        private static Color CenterPixel(System.Windows.Media.Imaging.BitmapSource image)
        {
            if (image == null) throw new InvalidOperationException("No background frame received.");
            var bytes = new byte[4]; image.CopyPixels(new W.Int32Rect(image.PixelWidth / 2, image.PixelHeight / 2, 1, 1), bytes, 4, 0);
            return Color.FromArgb(bytes[2], bytes[1], bytes[0]);
        }
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
        [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
        private static Color Sample(W.Window window)
        {
            var scale = W.PresentationSource.FromVisual(window).CompositionTarget.TransformToDevice;
            IntPtr hwnd = new W.Interop.WindowInteropHelper(window).Handle, dc = GetDC(hwnd);
            try { uint p = GetPixel(dc, (int)(550 * scale.M11), (int)(55 * scale.M22)); return Color.FromArgb((int)(p & 255), (int)((p >> 8) & 255), (int)((p >> 16) & 255)); }
            finally { ReleaseDC(hwnd, dc); }
        }
        internal static int Fixture(string path)
        {
            var app = new W.Application();
            var fixture = new W.Window { Title = "MonitorShade backdrop test fixture", WindowStyle = W.WindowStyle.None, Width = 850, Height = 800, Left = 400, Top = 200, Background = M.Brushes.Red, WindowStartupLocation = W.WindowStartupLocation.Manual };
            var field = new C.Border { Background = M.Brushes.Red }; fixture.Content = field;
            var timer = new W.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) }; bool first = true; string previous = "";
            timer.Tick += delegate {
                if (first) { first = false; fixture.Hide(); fixture.Show(); ShowWindow(new W.Interop.WindowInteropHelper(fixture).Handle, 5); fixture.Topmost = true; fixture.Activate(); }
                if (!File.Exists(path)) { fixture.Close(); return; }
                var rendered = Sample(fixture);
                if (rendered.R > 220 && rendered.G < 40 && rendered.B < 40 && !File.Exists(path + ".ready")) File.WriteAllText(path + ".ready", "ready");
                try { using (var input = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))) {
                    string color = input.ReadToEnd(); if (color == previous) return; previous = color;
                    fixture.Background = color == "Blue" ? M.Brushes.Blue : color == "White" ? M.Brushes.White : M.Brushes.Red;
                    field.Background = color == "Split" ? (M.Brush)new M.LinearGradientBrush(new M.GradientStopCollection { new M.GradientStop(M.Colors.Red, 0), new M.GradientStop(M.Colors.Red, .5), new M.GradientStop(M.Colors.Blue, .5), new M.GradientStop(M.Colors.Blue, 1) }, 0) : fixture.Background;
                } } catch (IOException) { }
            };
            fixture.Loaded += delegate { ShowWindow(new W.Interop.WindowInteropHelper(fixture).Handle, 5); fixture.Activate(); timer.Start(); };
            app.Run(fixture); timer.Stop(); return 0;
        }

        internal static Bitmap CapturePixels(W.FrameworkElement text, W.Window window)
        {
            var origin = text.PointToScreen(new W.Point());
            var client = window.PointToScreen(new W.Point());
            var scale = W.PresentationSource.FromVisual(text).CompositionTarget.TransformToDevice;
            int width = (int)Math.Ceiling(text.ActualWidth * scale.M11), height = (int)Math.Ceiling(text.ActualHeight * scale.M22);
            var bitmap = new Bitmap(width, height);
            try
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
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }

        private static int ColoredPixels(W.FrameworkElement text, W.Window window, string imagePath)
        {
            using (var bitmap = CapturePixels(text, window))
            {
                bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
                int colored = 0, ink = 0;
                for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
                {
                    var p = bitmap.GetPixel(x, y);
                    if (p.R < 180 && p.G < 180 && p.B < 180) ink++;
                    if (Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B)) > 12) colored++;
                }
                if (ink < 30) throw new InvalidOperationException("Text capture contained no usable glyphs; the desktop may be unavailable.");
                return colored;
            }
        }

        internal static int RunSettings(string path, bool pretendard)
        {
            var app = new W.Application { ShutdownMode = W.ShutdownMode.OnExplicitShutdown };
            try
            {
                if (!DesktopAccess.Available()) throw new InvalidOperationException("Interactive desktop is unavailable.");
                SetThreadExecutionState(0x80000003);
                using (var window = new SettingsForm(true, false, true))
                {
                    window.Topmost = true; window.Show(); Wait(200);
                    window.Hide(); window.Show(); ShowWindow(window.Handle, 5); window.Activate(); Wait(300);
                    return window.VerifyLiveText(path, pretendard);
                }
            }
            catch (Exception e) { File.WriteAllText(path, "FAIL: " + e); return 1; }
            finally { SetThreadExecutionState(0x80000000); app.Shutdown(); }
        }

        internal static int Run(string path)
        {
            var results = new List<string>();
            var application = new W.Application { ShutdownMode = W.ShutdownMode.OnExplicitShutdown };
            System.Diagnostics.Process fixtureProcess = null;
            string fixtureFile = path + "." + Guid.NewGuid().ToString("N") + ".fixture.txt";
            try
            {
                // Let the user dismiss a lock/screensaver themselves. Once available,
                // keep only this short live test from triggering another idle timeout.
                for (int n = 0; n < 100 && !DesktopAccess.Available(); n++) Wait(500);
                if (!DesktopAccess.Available()) throw new InvalidOperationException("Windows is not displaying the interactive desktop. Unlock/dismiss the system screensaver before the live test.");
                SetThreadExecutionState(0x80000003);
                C.UserControl template;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MonitorShade.Settings.xaml")) template = (C.UserControl)W.Markup.XamlReader.Load(stream);
                var panel = new C.StackPanel { Margin = new W.Thickness(20), Resources = template.Resources };
                panel.SetValue(W.Documents.TextElement.FontFamilyProperty, TextPresentation.DesignFont);
                panel.Resources["Card"] = new M.SolidColorBrush(M.Color.FromArgb(210, 255, 255, 255));
                panel.Resources["Text"] = M.Brushes.Black;
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
                File.WriteAllText(fixtureFile, "Red");
                fixtureProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath, "--backdrop-fixture \"" + fixtureFile + "\"") { UseShellExecute = false, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden });
                for (int n = 0; n < 50 && !File.Exists(fixtureFile + ".ready"); n++) Wait(100);
                if (!File.Exists(fixtureFile + ".ready")) throw new InvalidOperationException("The test fixture did not render its red background.");
                using (var window = new ProbeWindow { Title = "MonitorShade text rendering verification", Width = 620, Height = 650, Left = 515, Top = 275, Background = M.Brushes.Transparent, Topmost = true, WindowStartupLocation = W.WindowStartupLocation.Manual })
                {
                    bool suspended = false;
                    var ui = new C.UserControl { Content = new C.ScrollViewer { Content = panel }, Background = new M.SolidColorBrush(M.Color.FromArgb(194, 210, 210, 210)), UseLayoutRounding = true, SnapsToDevicePixels = true };
                    using (var backdrop = new CompositedBackdrop(window, ui, () => suspended, delegate { }))
                    {
                    window.Content = backdrop.Root;
                    window.Show(); Wait(100); window.Hide(); window.Show(); ShowWindow(window.Handle, 5); window.Activate();
                    backdrop.Apply(true, false); TextPresentation.Apply(backdrop.Root, true, true);
                    M.RenderOptions.SetClearTypeHint(panel, M.ClearTypeHint.Enabled);
                    Wait(1800);
                    if (!backdrop.Active || backdrop.FrameCount < 2) throw new InvalidOperationException("Backdrop capture not active: " + backdrop.Error);
                    var preview = new System.Windows.Media.Imaging.PngBitmapEncoder(); preview.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(backdrop.BackgroundFrame));
                    using (var output = File.Create(path + ".background.png")) preview.Save(output);
                    var red = CenterPixel(backdrop.BackgroundFrame);
                    var redCard = Sample(window);
                    if (red.R < 220 || red.B > 40 || red.G > 40) throw new InvalidOperationException("Self-exclusion did not reveal the red background: " + red + " " + backdrop.Diagnostic);
                    results.Add("PASS: Downsampled live background excludes the settings window and its capture helper");
                    File.WriteAllText(fixtureFile, "Blue"); Wait(900);
                    var blue = CenterPixel(backdrop.BackgroundFrame);
                    var blueCard = Sample(window);
                    if (blue.B < 220 || blue.R > 40 || blue.G > 40) throw new InvalidOperationException("Behind-window changes were not captured: " + blue);
                    results.Add("PASS: Background updates from red to blue without self-feedback");
                    if (redCard.R <= blueCard.R || blueCard.B <= redCard.B) throw new InvalidOperationException("Rendered card did not transmit the background tint: " + redCard + " -> " + blueCard);
                    results.Add("PASS: Rendered translucent card pixels change with the real background: " + redCard + " -> " + blueCard);
                    File.WriteAllText(fixtureFile, "White"); Wait(900);
                    results.Add("INFO: Background frame=" + backdrop.BackgroundFrame.PixelWidth + "x" + backdrop.BackgroundFrame.PixelHeight);
                    for (int i = 0; i < samples.Count; i += 2)
                    {
                        int clear = ColoredPixels(samples[i], window, path + "." + i + "-cleartype.png");
                        int gray = ColoredPixels(samples[i + 1], window, path + "." + i + "-grayscale.png");
                        bool pass = clear > 30 && gray < clear / 5;
                        results.Add((pass ? "PASS: " : "FAIL: ") + "Pretendard " + samples[i].FontSize + " DIP on a translucent (210/255) card over live composed blur: subpixel edges=" + clear + ", grayscale control=" + gray);
                        if (!pass) throw new InvalidOperationException("ClearType pixels could not be distinguished from the grayscale control.");
                    }
                    results.Add("INFO: Effective display DPI=" + (W.PresentationSource.FromVisual(window).CompositionTarget.TransformToDevice.M11 * 96));
                    File.WriteAllText(fixtureFile, "Split"); double originalLeft = window.Left;
                    window.Left = originalLeft - 60; Wait(700); var left = CenterPixel(backdrop.BackgroundFrame);
                    window.Left = originalLeft + 60; Wait(700); var right = CenterPixel(backdrop.BackgroundFrame);
                    if (left.R < 220 || right.B < 220) throw new InvalidOperationException("Capture did not track window movement: " + left + " / " + right);
                    results.Add("PASS: Moving the window samples the matching desktop coordinates");
                    window.Left = originalLeft; int previousWidth = backdrop.BackgroundFrame.PixelWidth;
                    window.Width = 580; Wait(1000);
                    if (backdrop.BackgroundFrame == null || backdrop.BackgroundFrame.PixelWidth >= previousWidth) throw new InvalidOperationException("Capture did not resize.");
                    results.Add("PASS: Resizing rebuilds the background capture at the matching size");
                    window.WindowState = W.WindowState.Minimized; Wait(250);
                    if (backdrop.Running) throw new InvalidOperationException("Minimized window continued capturing.");
                    window.WindowState = W.WindowState.Normal; Wait(1200);
                    if (!backdrop.Running || backdrop.BackgroundFrame == null) throw new InvalidOperationException("Restore did not resume capture.");
                    results.Add("PASS: Minimize stops capture and restore restarts it");
                    suspended = true; Wait(250);
                    if (backdrop.Running || backdrop.BackgroundFrame != null) throw new InvalidOperationException("Capture did not stop on suspension.");
                    results.Add("PASS: Session suspension stops capture and clears the background frame");
                    suspended = false; Wait(1200);
                    if (!backdrop.Running || backdrop.BackgroundFrame == null) throw new InvalidOperationException("Capture did not resume.");
                    window.Hide(); Wait(250);
                    if (backdrop.Running || backdrop.BackgroundFrame != null) throw new InvalidOperationException("Hidden window continued capturing.");
                    results.Add("PASS: Hiding the settings window stops capture and clears the background frame");
                    window.Show(); Wait(1200);
                    if (!backdrop.Running || backdrop.BackgroundFrame == null) throw new InvalidOperationException("Reopening did not resume capture.");
                    results.Add("PASS: Reopening resumes the composed background");
                    backdrop.Apply(false, false); Wait(250);
                    if (backdrop.Running || backdrop.Active) throw new InvalidOperationException("Blur toggle did not stop capture.");
                    results.Add("PASS: Disabling blur releases capture resources");
                    }
                }
                File.WriteAllLines(path, results); return 0;
            }
            catch (Exception e) { results.Add("FAIL: " + e); File.WriteAllLines(path, results); return 1; }
            finally { File.Delete(fixtureFile); if (fixtureProcess != null) { if (!fixtureProcess.WaitForExit(2000)) fixtureProcess.Kill(); fixtureProcess.Dispose(); } File.Delete(fixtureFile + ".ready"); SetThreadExecutionState(0x80000000); application.Shutdown(); }
        }
    }
}
