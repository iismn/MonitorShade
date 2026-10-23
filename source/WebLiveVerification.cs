using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace MonitorShade
{
    // Opt-in screenshot test of our own window above a disposable color fixture.
    // Neither fixture nor capture is used by the shipping settings window.
    internal static class WebLiveVerification
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(System.Drawing.Point point);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref System.Drawing.Point point);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr ChildWindowFromPointEx(IntPtr hwnd, System.Drawing.Point point, uint flags);
        internal static void CheckResizeEdges(SettingsForm form, List<string> report, bool visible)
        {
            double w = form.ActualWidth, h = form.ActualHeight;
            var points = new[] { new Point(3, h / 2), new Point(w - 3, h / 2), new Point(w / 2, 3), new Point(w / 2, h - 3), new Point(5, 5), new Point(w - 5, 5), new Point(5, h - 5), new Point(w - 5, h - 5) };
            int[] expected = { 10, 11, 12, 15, 13, 14, 16, 17 };
            for (int i = 0; i < points.Length; i++) {
                var p = form.PointToScreen(points[i]);
                var physical = new System.Drawing.Point((int)p.X, (int)p.Y);
                var client = physical; ScreenToClient(form.Handle, ref client);
                IntPtr hitWindow = visible ? WindowFromPoint(physical) : ChildWindowFromPointEx(form.Handle, client, 0);
                int packed = (physical.X & 0xffff) | ((physical.Y & 0xffff) << 16);
                int hit = SendMessage(hitWindow, 0x84, IntPtr.Zero, new IntPtr(packed)).ToInt32();
                if (hitWindow != form.Handle || hit != expected[i]) {
                    var name = new System.Text.StringBuilder(256); LayoutNative.GetClassName(hitWindow, name, name.Capacity);
                    throw new InvalidOperationException("Native resize edge " + i + " at " + physical + " hit=" + hit + ", class=" + name + ", expected hwnd=" + form.Handle + ", actual=" + hitWindow + ", active=" + form.IsActive + ", visible=" + form.IsVisible);
                }
            }
            report.Add("PASS: All four edges and four corners reach native Windows resize hit testing");
        }
        private static void CopyScreen(System.Drawing.Graphics graphics, int x, int y, int width, int height)
        {
            IntPtr source = Native.GetDC(IntPtr.Zero), destination = graphics.GetHdc();
            try {
                if (source == IntPtr.Zero || !Native.BitBlt(destination, 0, 0, width, height, source, x, y, 0x40CC0020))
                    throw new InvalidOperationException("Cannot read the composed desktop pixels.");
            } finally { graphics.ReleaseHdc(destination); if (source != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, source); }
        }
        private static Color ScreenPixel(Window window, double x = .92, double y = .85)
        {
            var point = window.PointToScreen(new Point(window.ActualWidth * x, window.ActualHeight * y));
            using (var image = new System.Drawing.Bitmap(1, 1))
            using (var graphics = System.Drawing.Graphics.FromImage(image)) {
                CopyScreen(graphics, (int)point.X, (int)point.Y, 1, 1);
                var color = image.GetPixel(0, 0); return Color.FromRgb(color.R, color.G, color.B);
            }
        }
        private static void SaveScreen(Window window, string path)
        {
            var origin = window.PointToScreen(new Point(0, 0));
            var bottom = window.PointToScreen(new Point(window.ActualWidth, window.ActualHeight));
            using (var image = new System.Drawing.Bitmap((int)(bottom.X - origin.X), (int)(bottom.Y - origin.Y)))
            using (var graphics = System.Drawing.Graphics.FromImage(image)) {
                CopyScreen(graphics, (int)origin.X, (int)origin.Y, image.Width, image.Height);
                image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        internal static int Run(string path)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var report = new List<string>(); int result = 1;
            Window fixture = null; SettingsForm form = null;
            app.Startup += async delegate {
                try {
                    if (!DesktopAccess.Available()) throw new InvalidOperationException("Interactive desktop unavailable; live blur verification requires an unlocked desktop.");
                    if (!DesktopBackdrop.TransparencyEnabled) throw new InvalidOperationException("Windows Acrylic transparency is unavailable.");
                    fixture = new Window { Title = "MonitorShade native blur test fixture", WindowStyle = WindowStyle.None, ShowInTaskbar = false, Topmost = true, Left = 100, Top = 100, Width = 1300, Height = 950, Background = Brushes.Red };
                    var fixtureContent = new System.Windows.Controls.Border();
                    fixtureContent.SetBinding(System.Windows.Controls.Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { Source = fixture });
                    fixture.Content = fixtureContent;
                    fixture.Show(); await Task.Delay(150); fixture.Hide(); fixture.Show();
                    // The test process is launched with STARTUPINFO's hidden-window flag.
                    // A second explicit ShowWindow makes the fixture visible as intended.
                    ShowWindow(new System.Windows.Interop.WindowInteropHelper(fixture).Handle, 5);
                    fixture.Activate(); await Task.Delay(500);
                    var fixtureColor = ScreenPixel(fixture);
                    if (fixtureColor.R < 240 || fixtureColor.B > 15 || fixtureColor.G > 15) throw new InvalidOperationException("The background fixture is not visible: " + fixtureColor);
                    report.Add("PASS: Color fixture is visible on the interactive desktop");
                    form = new SettingsForm(true, false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = 140, Top = 140 };
                    fixture.Topmost = false;
                    form.Show(); ShowWindow(form.Handle, 5); form.Activate();
                    SetWindowPos(new System.Windows.Interop.WindowInteropHelper(fixture).Handle, form.Handle, 0, 0, 0, 0, 0x13);
                    for (int i = 0; i < 150 && !form.BrowserUi.Ready && form.BrowserUi.Error == null; i++) await Task.Delay(100);
                    if (!form.BrowserUi.Ready) throw new InvalidOperationException(form.BrowserUi.Error ?? "Renderer timeout");
                    form.WebCommand(new Dictionary<string, object> { { "action", "field" }, { "name", "AppearanceMode" }, { "value", 3 } });
                    await Task.Delay(2500);
                    CheckResizeEdges(form, report, true);
                    // Window-only WGC / browser captures omit the DWM backdrop. Sample
                    // the final desktop pixels inside our unobscured test window.
                    var red = ScreenPixel(form);
                    var fixtureRed = ScreenPixel(fixture, .01, .01);
                    fixture.Background = Brushes.Blue;
                    await Task.Delay(1000);
                    var blue = ScreenPixel(form);
                    var fixtureBlue = ScreenPixel(fixture, .01, .01);
                    int actualBackdrop; DwmGetWindowAttribute(form.Handle, 38, out actualBackdrop, 4);
                    report.Add("INFO: Screen background red=" + red + ", blue=" + blue + ", active=" + form.IsActive + ", backdrop=" + actualBackdrop);
                    report.Add("INFO: Fixture=" + fixtureRed + "/" + fixtureBlue + ", desktop=" + DesktopAccess.Available());
                    if (fixtureRed.R < 240 || fixtureBlue.B < 240 || !DesktopAccess.Available()) throw new InvalidOperationException("The color fixture or interactive desktop was obscured during the test.");
                    if (red.R <= blue.R + 2 || blue.B <= red.B + 2) throw new InvalidOperationException("Native Acrylic did not transmit the changing background.");
                    report.Add("PASS: Chromium transparent surface transmits native DWM Acrylic background changes");
                    form.Left += 90; form.Top += 20; await Task.Delay(500);
                    fixture.Background = Brushes.Red; await Task.Delay(500);
                    var moved = ScreenPixel(form);
                    if (moved.R <= blue.R + 2) throw new InvalidOperationException("Background stopped updating after movement");
                    report.Add("PASS: Native background remains live after moving the settings window");
                    var field = typeof(SettingsForm).GetField("backdrop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field.GetValue(form) != null) throw new InvalidOperationException("Legacy capture compositor was created");
                    report.Add("PASS: No Magnifier capture helper or legacy compositor is constructed");
                    fixture.Background = new SolidColorBrush(Color.FromRgb(37, 59, 72)); await Task.Delay(600);
                    SaveScreen(form, path + ".png");
                    form.Hide(); await Task.Delay(400); form.Show(); form.Activate(); await Task.Delay(500);
                    if (await form.BrowserUi.Core.ExecuteScriptAsync("Boolean(window.monitorShadeState && !window.monitorShadeError)") != "true") throw new InvalidOperationException("Renderer failed to resume");
                    report.Add("PASS: Chromium resumes correctly after hiding and reopening");
                    CheckResizeEdges(form, report, true);
                    form.WindowState = WindowState.Maximized; await Task.Delay(350);
                    if (form.BrowserUi.Core == null) throw new InvalidOperationException("Renderer lost after maximizing");
                    form.WindowState = WindowState.Normal; await Task.Delay(600);
                    CheckResizeEdges(form, report, true);
                    report.Add("PASS: Native resize edges survive maximize and restore");
                    result = 0;
                } catch (Exception e) { report.Add("FAIL: " + e); }
                finally { if (form != null) form.Dispose(); if (fixture != null) fixture.Close(); File.WriteAllLines(path, report); app.Shutdown(); }
            };
            app.Run(); return result;
        }
    }
}
