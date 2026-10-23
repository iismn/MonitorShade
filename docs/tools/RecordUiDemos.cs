// Documentation-only recorder. Loads the released EXE in its existing preview mode.
// Sliders use the real WebView2 -> C# -> WebView2 command path. No hardware writes.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

internal static class RecordUiDemos
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Window window;
    private static object ui;
    private static Type formType;
    private static CoreWebView2 core;
    private static string output;
    private static readonly System.Collections.Generic.List<string> report = new System.Collections.Generic.List<string>();

    [STAThread]
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error) {
            if (args.Length == 2) File.WriteAllText(Path.Combine(args[1], "capture-report.txt"), error.ToString());
            return 1;
        }
    }
    private static int Run(string[] args)
    {
        if (args.Length != 2) return 2;
        output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        assembly.GetType("MonitorShade.WebRuntime").GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        formType = assembly.GetType("MonitorShade.SettingsForm");
        window = (Window)Activator.CreateInstance(formType, new object[] { true, false, false });
        window.Width = 1120; window.Height = 840;
        window.ShowInTaskbar = false;
        int result = 1;
        window.Loaded += async delegate {
            try {
                ui = formType.GetProperty("BrowserUi", Instance).GetValue(window, null);
                for (int i = 0; i < 200 && !(bool)ui.GetType().GetProperty("Ready", Instance).GetValue(ui, null); i++) await Task.Delay(100);
                if (!(bool)ui.GetType().GetProperty("Ready", Instance).GetValue(ui, null)) throw new Exception("WebView2 did not become ready.");
                core = (CoreWebView2)ui.GetType().GetProperty("Core", Instance).GetValue(ui, null);
                await Task.Delay(1000);
                await Script("document.querySelector('#AppearanceMode').value='1';document.querySelector('#AppearanceMode').dispatchEvent(new Event('change'));document.querySelector('#masterRange').value=45;document.querySelector('#masterRange').dispatchEvent(new Event('input',{bubbles:true}))");
                // WebView2 captures do not contain the DWM surface. Use the app's
                // own background variable as the base, exactly as in its UI tests.
                await Script("document.documentElement.style.background='rgb(var(--background))'");
                await Task.Delay(1500);
                await Check("monitorShadeState.preview && monitorShadeState.monitors.length===2", "Released app UI is in preview mode.");
                await Check("monitorShadeState.monitors.every(m=>m.percent===45)", "Native brightness controller synchronized both demo monitors.");
                await RecordBrightness();
                await RecordTime();
                foreach (var sample in new[] { new { Name = "morning", Hour = 9.0 }, new { Name = "day", Hour = 14.0 }, new { Name = "evening", Hour = 20.0 }, new { Name = "night", Hour = 0.0 } }) {
                    SetHour(sample.Hour); await Task.Delay(1500);
                    await Capture(Path.Combine(output, "appearance-" + sample.Name + ".png"));
                }
                await Check("!window.monitorShadeError", "No renderer errors during recording.");
                report.Add("Captured from the released EXE; preview monitors; accelerated local clock; DWM not included.");
                result = 0;
            } catch (Exception error) { report.Add(error.ToString()); }
            finally {
                File.WriteAllLines(Path.Combine(output, "capture-report.txt"), report);
                formType.GetMethod("AllowSystemExit", Instance).Invoke(window, null);
                window.Close(); app.Shutdown();
            }
        };
        window.Show(); app.Run();
        return result;
    }
    private static Task<string> Script(string js) { return core.ExecuteScriptAsync(js); }
    private static async Task Check(string expression, string description)
    {
        if (await Script("Boolean(" + expression + ")") != "true") throw new Exception(description);
        report.Add("PASS: " + description);
    }
    private static void Publish() { ui.GetType().GetMethod("Publish", Instance).Invoke(ui, null); }
    private static void SetHour(double hour)
    {
        formType.GetField("previewHour", Instance).SetValue(window, (double?)(hour % 24));
        formType.GetMethod("ApplyTheme", Instance).Invoke(window, new object[] { null });
        Publish();
    }
    private static async Task Capture(string path)
    {
        using (var file = File.Create(path)) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
    }
    private static async Task Frame(string folder, int number, Action action, Func<Task> asyncAction)
    {
        var timer = Stopwatch.StartNew();
        if (action != null) action();
        if (asyncAction != null) await asyncAction();
        await Task.Delay(35);
        await Capture(Path.Combine(output, folder, number.ToString("D4") + ".png"));
        // Record real rendered frames at 8 fps; no generated in-between images.
        if (timer.ElapsedMilliseconds < 125) await Task.Delay((int)(125 - timer.ElapsedMilliseconds));
    }
    private static int Interpolate(int frame, int start, int end, int from, int to)
    {
        double t = Math.Max(0, Math.Min(1, (frame - start) / (double)(end - start)));
        return (int)Math.Round(from + (to - from) * (t * t * (3 - 2 * t)));
    }
    private static async Task RecordBrightness()
    {
        Directory.CreateDirectory(Path.Combine(output, "brightness"));
        int last = -1;
        for (int frame = 0; frame < 104; frame++) {
            int value = frame < 40 ? Interpolate(frame, 10, 35, 45, 80)
                : frame < 72 ? Interpolate(frame, 46, 68, 80, 30)
                : Interpolate(frame, 78, 96, 30, 45);
            await Frame("brightness", frame, null, async delegate {
                if (value == last) return;
                await Script("document.querySelector('#masterRange').value=" + value + ";document.querySelector('#masterRange').dispatchEvent(new Event('input',{bubbles:true}))");
                Publish(); last = value;
            });
        }
        await Check("monitorShadeState.monitors.every(m=>m.percent===45)", "Recorded slider ends with both monitors at 45%.");
    }
    private static async Task RecordTime()
    {
        Directory.CreateDirectory(Path.Combine(output, "appearance"));
        await Script("document.querySelector('#masterRange').value=70;document.querySelector('#masterRange').dispatchEvent(new Event('input',{bubbles:true}))");
        SetHour(9); await Task.Delay(1000);
        // One real palette calculation per frame. 24 hours in a 20-second loop.
        for (int frame = 0; frame < 160; frame++) {
            double hour = 9 + Math.Max(0, Math.Min(1, (frame - 8) / 144.0)) * 24;
            await Frame("appearance", frame, delegate { SetHour(hour); }, null);
        }
        await Check("monitorShadeState.time.startsWith('09:00')", "Accelerated clock completed a 24-hour loop.");
    }
}
