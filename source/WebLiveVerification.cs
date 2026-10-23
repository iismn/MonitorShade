using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MonitorShade
{
    // Opt-in screenshot test of our own window above a disposable color fixture.
    // Neither fixture nor capture is used by the shipping settings window.
    internal static class WebLiveVerification
    {
        private static Color Pixel(BitmapSource image)
        {
            byte[] bytes = new byte[4]; image.CopyPixels(new Int32Rect((int)(image.PixelWidth * .92), (int)(image.PixelHeight * .85), 1, 1), bytes, 4, 0);
            return Color.FromRgb(bytes[2], bytes[1], bytes[0]);
        }
        internal static int Run(string path)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var report = new List<string>(); int result = 1;
            Window fixture = null; SettingsForm form = null; BackdropCapture capture = null;
            app.Startup += async delegate {
                try {
                    if (!DesktopAccess.Available()) throw new InvalidOperationException("Interactive desktop unavailable; live blur verification requires an unlocked desktop.");
                    if (!DesktopBackdrop.TransparencyEnabled) throw new InvalidOperationException("Windows Acrylic transparency is unavailable.");
                    fixture = new Window { Title = "MonitorShade native blur test fixture", WindowStyle = WindowStyle.None, ShowInTaskbar = false, Left = 100, Top = 100, Width = 1300, Height = 950, Background = Brushes.Red };
                    fixture.Show();
                    form = new SettingsForm(true, false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = 140, Top = 140, Topmost = true };
                    form.Show(); form.Activate();
                    for (int i = 0; i < 150 && !form.BrowserUi.Ready && form.BrowserUi.Error == null; i++) await Task.Delay(100);
                    if (!form.BrowserUi.Ready) throw new InvalidOperationException(form.BrowserUi.Error ?? "Renderer timeout");
                    form.WebCommand(new Dictionary<string, object> { { "action", "field" }, { "name", "AppearanceMode" }, { "value", 3 } });
                    await Task.Delay(1200);
                    BitmapSource frame = null; string error = null;
                    capture = new BackdropCapture(form.Handle, image => { frame = image; }, message => { error = message; });
                    for (int i = 0; i < 60 && frame == null && error == null; i++) await Task.Delay(100);
                    if (frame == null) throw new InvalidOperationException(error ?? "No captured frame");
                    var red = Pixel(frame);
                    fixture.Background = Brushes.Blue;
                    await Task.Delay(1000);
                    var blue = Pixel(frame);
                    report.Add("INFO: Native background red=" + red + ", blue=" + blue);
                    if (red.R <= blue.R + 2 || blue.B <= red.B + 2) throw new InvalidOperationException("Native Acrylic did not transmit the changing background.");
                    report.Add("PASS: Chromium transparent surface transmits native DWM Acrylic background changes");
                    var originalLeft = form.Left;
                    form.Left += 90; form.Top += 20; await Task.Delay(500);
                    fixture.Background = Brushes.Red; await Task.Delay(500);
                    var moved = Pixel(frame);
                    if (moved.R <= blue.R + 2) throw new InvalidOperationException("Background stopped updating after movement");
                    report.Add("PASS: Native background remains live after moving the settings window");
                    var field = typeof(SettingsForm).GetField("backdrop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field.GetValue(form) != null) throw new InvalidOperationException("Legacy capture compositor was created");
                    report.Add("PASS: No Magnifier capture helper or legacy compositor is constructed");
                    fixture.Background = new SolidColorBrush(Color.FromRgb(37, 59, 72)); await Task.Delay(600);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(frame)); using (var file = File.Create(path + ".png")) png.Save(file);
                    capture.Dispose(); capture = null;
                    form.Hide(); await Task.Delay(400); form.Show(); form.Activate(); await Task.Delay(500);
                    if (await form.BrowserUi.Core.ExecuteScriptAsync("Boolean(window.monitorShadeState && !window.monitorShadeError)") != "true") throw new InvalidOperationException("Renderer failed to resume");
                    report.Add("PASS: Chromium resumes correctly after hiding and reopening");
                    result = 0;
                } catch (Exception e) { report.Add("FAIL: " + e); }
                finally { if (capture != null) capture.Dispose(); if (form != null) form.Dispose(); if (fixture != null) fixture.Close(); File.WriteAllLines(path, report); app.Shutdown(); }
            };
            app.Run(); return result;
        }
    }
}
