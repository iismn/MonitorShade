using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
[assembly: System.Reflection.AssemblyTitle("MonitorShade")]
[assembly: System.Reflection.AssemblyVersion("1.17.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.17.0.0")]

namespace MonitorShade
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            WebRuntime.Register();
            return Run(args);
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int Run(string[] args)
        {
            bool automatic = !args.Contains("--settings");
            Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--web-ui-test") return WebUiVerification.Run(args[1]);
            if (args.Length == 2 && args[0] == "--web-live-test") return WebLiveVerification.Run(args[1]);
            if (args.Length == 1 && args[0] == "--design-preview")
            {
                var demo = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose };
                using (var form = new SettingsForm(true, false)) {
                    form.Loaded += delegate { LayoutNative.ShowWindowAsync(form.Handle, 5); };
                    demo.Run(form);
                }
                return 0;
            }
            if (args.Length == 3 && args[0] == "--ddc-worker") return DdcWorker.Run(args[1], args[2]);
            if (args.Length == 2 && args[0] == "--brightness-test") return BrightnessVerification.Run(args[1]);
            if (args.Length == 2 && args[0] == "--appearance-test") return AppearanceVerification.Run(args[1]);
            if (args.Length == 2 && args[0] == "--text-test") return TextRasterVerification.Run(args[1]);
            if (args.Length == 2 && args[0] == "--settings-text-test") return TextRasterVerification.RunSettings(args[1], false);
            if (args.Length == 2 && args[0] == "--settings-text-pretendard-test") return TextRasterVerification.RunSettings(args[1], true);
            if (args.Length == 2 && args[0] == "--backdrop-fixture") return TextRasterVerification.Fixture(args[1]);
            if (args.Length == 2 && args[0] == "--recovery-test") return RecoveryVerification.Run(args[1]);
            if (args.Length == 2 && args[0] == "--reconnect-test") return DisplayRecoveryVerification.Run(args[1]);
            if (args.Length == 2 && args[0] == "--manual-test") return ManualShadeVerification.Run(args[1], false);
            if (args.Length == 2 && args[0] == "--manual-live-test") return ManualShadeVerification.Run(args[1], true);
            if (args.Length == 2 && args[0] == "--wallpaper-test") return WallpaperVerification.Run(args[1], false);
            if (args.Length == 2 && args[0] == "--wallpaper-live-test") return WallpaperVerification.Run(args[1], true);
            if (args.Length == 2 && args[0] == "--configure-all")
            {
                Preferences p = Preferences.Read(args[1]);
                ConnectedDisplay[] connected = ConnectedDisplay.Read();
                p.DeviceNames = connected.Select(s => s.DeviceName).ToArray();
                p.DeviceName = p.DeviceNames.FirstOrDefault() ?? "";
                p.WatchDisplays = new RememberedDisplay[0];
                var selection = new DisplaySelection(p); selection.Resolve(connected); selection.Store(p);
                p.Write(args[1]); return 0;
            }
            if (args.Length == 2 && args[0] == "--logic-test") return Verification.RunLogic(args[1]);
            if (args.Length == 2 && args[0] == "--layout-test") return LayoutVerification.Run(args[1], false);
            if (args.Length == 2 && args[0] == "--layout-live-test") return LayoutVerification.Run(args[1], true);
            if (args.Length == 2 && args[0] == "--test") return Verification.Run(args[1]);
            if (args.Length == 2 && (args[0] == "--preview" || args[0] == "--preview-light" || args[0] == "--ui-test"))
            {
                try
                {
                    var previewApplication = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                    previewApplication.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
                        File.WriteAllText(args[1] + ".error.txt", e.Exception.ToString());
                    };
                    using (SettingsForm form = new SettingsForm(true, false, true))
                    {
                        form.Show();
                        Application.DoEvents();
                        if (args[0] == "--ui-test") { form.Hide(); form.Show(); form.Activate(); Application.DoEvents(); }
                        form.ExportPreview(args[1], args[0]);
                    }
                    previewApplication.Shutdown();
                    return 0;
                }
                catch (Exception e) { File.WriteAllText(args[1] + ".error.txt", e.ToString()); return 1; }
            }
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\MonitorShade.DesktopUtility.v1", out created))
            {
                if (!created)
                {
                    if (!automatic)
                    {
                        Native.AllowSetForegroundWindow(-1);
                        Native.PostMessage(new IntPtr(0xffff), Native.RegisterWindowMessage("MonitorShade.ShowSettings.v1"), IntPtr.Zero, IntPtr.Zero);
                    }
                    return 0;
                }
                if (Environment.OSVersion.Version.Build < 19041)
                {
                    MessageBox.Show("Windows 10 버전 2004 이상 또는 Windows 11이 필요합니다.", "MonitorShade");
                    return 1;
                }
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                var application = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                using (SettingsForm form = new SettingsForm(false, automatic))
                {
                    Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                    {
                        form.Pause("오류로 감시를 중지했습니다.");
                        MessageBox.Show(e.Exception.Message, "MonitorShade 오류");
                    };
                    application.SessionEnding += delegate { form.AllowSystemExit(); };
                    application.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                    { form.Pause("오류로 감시를 중지했습니다."); MessageBox.Show(e.Exception.Message, "MonitorShade 오류"); e.Handled = true; };
                    form.Closed += delegate { application.Shutdown(); };
                    if (!automatic) form.Show();
                    application.Run();
                }
                mutex.ReleaseMutex();
            }
            return 0;
        }
    }

    // A monotonic clock is supplied by the caller; wall-clock adjustments cannot blank a screen early.
    public sealed class IdlePolicy
    {
        private long lastActivity;
        public readonly long DelayMilliseconds;
        public IdlePolicy(long delayMilliseconds, long now)
        {
            if (delayMilliseconds < 1000) throw new ArgumentOutOfRangeException("delayMilliseconds");
            DelayMilliseconds = delayMilliseconds;
            lastActivity = now;
        }
        public void Activity(long now) { lastActivity = now; }
        public bool ShouldShade(long now, bool pointerInside) { return !pointerInside && now - lastActivity >= DelayMilliseconds; }
    }

    public sealed class Preferences
    {
        public string DeviceName = "";
        public string[] DeviceNames = new string[0];
        public RememberedDisplay[] WatchDisplays = new RememberedDisplay[0];
        public string[] SelectedDevices()
        {
            string[] names = (DeviceNames ?? new string[0]).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return names.Length > 0 ? names : (string.IsNullOrWhiteSpace(DeviceName) ? new string[0] : new[] { DeviceName });
        }
        public decimal WaitValue = 2;
        public bool Minutes = true;
        public bool IgnoreWallpaper = true;
        public bool LayoutEnabled = true;
        public int CenterPercent = 65;
        public BrightnessPreferences Brightness = new BrightnessPreferences();
        public ManualShadePreferences ManualShade = new ManualShadePreferences();
        public AppearancePreferences Appearance = new AppearancePreferences();
        public static Preferences Read(string path)
        {
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    Preferences p = (Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(stream);
                    if (p.WaitValue < 1 || p.WaitValue > 86400 || (p.Minutes && p.WaitValue > 1440)) return new Preferences();
                    if (p.CenterPercent < 10 || p.CenterPercent > 100) p.CenterPercent = 65;
                    if (p.Brightness == null) p.Brightness = new BrightnessPreferences();
                    if (p.ManualShade == null) p.ManualShade = new ManualShadePreferences();
                    if (p.Appearance == null) p.Appearance = new AppearancePreferences();
                    p.Appearance.Normalize();
                    p.Brightness.Normalize();
                    return p;
                }
            }
            catch { return new Preferences(); }
        }
        public void Write(string path)
        {
            string temporary = path + ".tmp";
            try
            {
                using (FileStream stream = File.Create(temporary)) new XmlSerializer(typeof(Preferences)).Serialize(stream, this);
                // Same-directory atomic rename avoids ReplaceFile's unnecessary ACL-copy privileges.
                if (!Native.MoveFileEx(temporary, path, 0x1 | 0x8)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public class PassiveWindow : Form
    {
        public PassiveWindow(Rectangle bounds, Color color)
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            BackColor = color;
            TopMost = true;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                p.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                return p;
            }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            base.WndProc(ref m);
        }
    }

    public sealed class ShadeWindow : PassiveWindow
    {
        public event EventHandler WakeRequested;
        public ShadeWindow(Rectangle bounds) : base(bounds, Color.Black)
        {
            if (!Native.SetWindowDisplayAffinity(Handle, 0x11))
            {
                int error = Marshal.GetLastWin32Error(); Dispose();
                throw new Win32Exception(error, "오버레이 캡처 제외를 설정하지 못했습니다.");
            }
        }
        protected override void OnMouseMove(MouseEventArgs e) { Wake(); base.OnMouseMove(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Wake(); base.OnMouseDown(e); }
        private void Wake() { if (WakeRequested != null) WakeRequested(this, EventArgs.Empty); }
        public void Cover()
        {
            Show();
            if (!Native.SetWindowPos(Handle, new IntPtr(-1), Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, 0x10 | 0x40))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    // Two reusable, full-resolution DIBs: no files, image compression, downsampling, or network traffic.
    public sealed class FrameCapture : IDisposable
    {
        private readonly Rectangle bounds;
        private IntPtr desktop;
        private readonly IntPtr[] dc = new IntPtr[2];
        private readonly IntPtr[] dib = new IntPtr[2];
        private readonly IntPtr[] old = new IntPtr[2];
        private readonly IntPtr[] pixels = new IntPtr[2];
        private int next;
        private bool initialized;
        public FrameCapture(Rectangle rectangle)
        {
            bounds = rectangle;
            try
            {
                desktop = Native.GetDC(IntPtr.Zero);
                if (desktop == IntPtr.Zero) throw new Win32Exception();
                Native.BitmapInfo info = new Native.BitmapInfo();
                info.Size = 40; info.Width = bounds.Width; info.Height = -bounds.Height;
                info.Planes = 1; info.BitCount = 32;
                for (int i = 0; i < 2; i++)
                {
                    dc[i] = Native.CreateCompatibleDC(desktop);
                    dib[i] = Native.CreateDIBSection(desktop, ref info, 0, out pixels[i], IntPtr.Zero, 0);
                    if (dc[i] == IntPtr.Zero || dib[i] == IntPtr.Zero) throw new Win32Exception();
                    old[i] = Native.SelectObject(dc[i], dib[i]);
                    if (old[i] == IntPtr.Zero || old[i] == new IntPtr(-1)) throw new Win32Exception();
                }
            }
            catch { Dispose(); throw; }
        }
        public bool CaptureChanged() { return CaptureChanged(null); }
        internal bool CaptureChanged(PixelSpan[] spans)
        {
            if (spans != null && spans.Length == 0) { initialized = false; return false; }
            if (!Native.BitBlt(dc[next], 0, 0, bounds.Width, bounds.Height, desktop, bounds.X, bounds.Y, 0x40CC0020))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "화면을 읽을 수 없어 감시를 중지했습니다.");
            Native.GdiFlush();
            bool changed = !initialized || (spans == null ? Different(pixels[0], pixels[1], checked(bounds.Width * bounds.Height)) : WallpaperMask.Different(pixels[0], pixels[1], spans));
            initialized = true;
            next = 1 - next;
            return changed;
        }
        internal static unsafe bool Different(IntPtr a, IntPtr b, int count)
        {
            uint* x = (uint*)a.ToPointer();
            uint* y = (uint*)b.ToPointer();
            // Ignore the undefined alpha byte in GDI's BI_RGB format; compare every RGB pixel.
            for (int i = 0; i < count; i++) if (((x[i] ^ y[i]) & 0x00FFFFFF) != 0) return true;
            return false;
        }
        public Color Pixel(int x, int y)
        {
            int rgb = Marshal.ReadInt32(pixels[1 - next], checked((y * bounds.Width + x) * 4));
            return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        }
        public void Dispose()
        {
            for (int i = 0; i < 2; i++)
            {
                if (old[i] != IntPtr.Zero && dc[i] != IntPtr.Zero) Native.SelectObject(dc[i], old[i]);
                if (dib[i] != IntPtr.Zero) Native.DeleteObject(dib[i]);
                if (dc[i] != IntPtr.Zero) Native.DeleteDC(dc[i]);
                old[i] = dib[i] = dc[i] = pixels[i] = IntPtr.Zero;
            }
            if (desktop != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, desktop);
            desktop = IntPtr.Zero;
        }
    }

    public sealed class WatchTarget
    {
        public string Name;
        public string Identity;
        public Rectangle Bounds;
    }

    // Each target owns its capture buffers, idle clock, mouse test and overlay.
    public sealed class WatchGroup : IDisposable
    {
        private readonly Dictionary<string, Watcher> members = new Dictionary<string, Watcher>();
        private readonly Dictionary<string, string> identities = new Dictionary<string, string>();
        private readonly HashSet<string> held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<Point> pointer;
        private readonly Func<bool, Watcher> factory;
        private int configuredSeconds;
        private bool configuredWallpaper;
        private bool suspended;
        public WatchGroup() { }
        internal WatchGroup(Func<Point> pointerReader) { pointer = pointerReader; }
        internal WatchGroup(Func<bool, Watcher> watcherFactory) { factory = watcherFactory; }
        public bool Running { get { return members.Values.Any(w => w.Running); } }
        public int RunningCount { get { return members.Values.Count(w => w.Running); } }
        public int WaitingCount { get { return members.Values.Count(w => w.Waiting); } }
        public bool Covered(string name) { return members.ContainsKey(name) && members[name].Covered; }
        public bool MatchesTargets(IEnumerable<WatchTarget> current)
        {
            var connected = current.ToDictionary(t => t.Name, t => t.Bounds, StringComparer.OrdinalIgnoreCase);
            return members.Count > 0 && members.All(pair => connected.ContainsKey(pair.Key) && connected[pair.Key] == pair.Value.Target);
        }
        public event Action<string> Failed;
        public event Action StateChanged;
        public void Start(IEnumerable<WatchTarget> targets, int seconds)
        { Start(targets, seconds, false); }
        public void Start(IEnumerable<WatchTarget> targets, int seconds, bool ignoreWallpaper)
        {
            Stop();
            WatchTarget[] requested = targets.ToArray();
            if (requested.Length == 0) throw new InvalidOperationException("감시할 모니터를 선택해 주세요.");
            Reconcile(requested, seconds, ignoreWallpaper);
        }
        internal void Reconcile(IEnumerable<WatchTarget> targets, int seconds, bool ignoreWallpaper)
        {
            if (seconds < 1 || seconds > 86400) throw new ArgumentOutOfRangeException("seconds");
            var requested = targets.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
            if (configuredSeconds != seconds || configuredWallpaper != ignoreWallpaper) Stop();
            configuredSeconds = seconds; configuredWallpaper = ignoreWallpaper;
            bool changed = false;
            foreach (string name in members.Keys.ToArray())
            {
                WatchTarget target;
                if (requested.TryGetValue(name, out target) && members[name].Target == target.Bounds &&
                    string.Equals(identities[name], target.Identity, StringComparison.OrdinalIgnoreCase)) continue;
                members[name].Dispose(); members.Remove(name); identities.Remove(name); changed = true;
            }
            foreach (WatchTarget target in requested.Values)
            {
                if (members.ContainsKey(target.Name)) continue;
                Watcher monitor = factory == null ? new Watcher(pointer, ignoreWallpaper) : factory(ignoreWallpaper);
                members.Add(target.Name, monitor); identities.Add(target.Name, target.Identity); changed = true;
                string name = target.Name;
                monitor.Failed += delegate(string message) { if (Failed != null) Failed(name + ": " + message); };
                monitor.StateChanged += delegate { if (StateChanged != null) StateChanged(); };
                try { monitor.Start(target.Bounds, seconds, suspended || held.Contains(target.Name)); }
                catch (Exception e) { monitor.Stop(); if (Failed != null) Failed(name + ": " + e.Message); }
            }
            if (changed && StateChanged != null) StateChanged();
        }
        internal void Hold(string device, bool value)
        {
            if (value) held.Add(device); else held.Remove(device);
            Watcher monitor; if (members.TryGetValue(device, out monitor)) monitor.Suspend(suspended || held.Contains(device));
        }
        internal bool Held(string device) { return held.Contains(device); }
        public void Suspend(bool value) { suspended = value; foreach (var pair in members) pair.Value.Suspend(value || held.Contains(pair.Key)); }
        public void Stop() { foreach (Watcher monitor in members.Values) monitor.Dispose(); members.Clear(); identities.Clear(); configuredSeconds = 0; }
        public void Dispose() { Stop(); }
    }

    internal static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] internal static extern bool AllowSetForegroundWindow(int processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool MoveFileEx(string existing, string replacement, uint flags);
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct BitmapInfo
        {
            internal uint Size; internal int Width, Height; internal ushort Planes, BitCount;
            internal uint Compression, SizeImage; internal int XPelsPerMeter, YPelsPerMeter;
            internal uint ClrUsed, ClrImportant, Color;
        }
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")] internal static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
        [DllImport("gdi32.dll")] internal static extern bool GdiFlush();
        [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
        [DllImport("dwmapi.dll")] internal static extern int DwmIsCompositionEnabled(out bool enabled);
    }

    internal static class Verification
    {
        private static readonly List<string> results = new List<string>();
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            results.Add("PASS: " + name);
        }
        private static void Pump(int milliseconds)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); }
            Native.DwmFlush();
        }
        internal static int Run(string resultPath)
        {
            results.Clear();
            try
            {
                LogicChecks(resultPath);

                foreach (Screen display in Screen.AllScreens)
                {
                    Rectangle area = new Rectangle(display.Bounds.X + 80, display.Bounds.Y + 80, 180, 120);
                    using (PassiveWindow background = new PassiveWindow(area, Color.FromArgb(32, 160, 72)))
                    using (FrameCapture capture = new FrameCapture(area))
                    using (ShadeWindow overlay = new ShadeWindow(area))
                    {
                        IntPtr focus = Native.GetForegroundWindow();
                        background.Show(); Pump(250);
                        capture.CaptureChanged(); capture.CaptureChanged();
                        Check(capture.Pixel(20, 20).G > 100, display.DeviceName + " capture sees test background");
                        overlay.Cover(); Pump(250);
                        Check(!capture.CaptureChanged(), display.DeviceName + " black overlay excluded without frame change");
                        Check(Native.GetForegroundWindow() == focus, display.DeviceName + " overlay does not steal focus");
                        background.BackColor = Color.FromArgb(180, 48, 60); background.Refresh(); Pump(250);
                        Check(capture.CaptureChanged() && capture.Pixel(20, 20).R > 140, display.DeviceName + " live underlying changes visible through overlay");
                        Check(!capture.CaptureChanged(), display.DeviceName + " stable frame remains unchanged");
                    }
                }
                // Exercise actual timer/overlay transitions on a controlled patch, without moving the user's mouse.
                Screen screen = Screen.AllScreens.OrderBy(s => s.Bounds.X).First();
                Rectangle patch = new Rectangle(screen.Bounds.X + 50, screen.Bounds.Y + 50, 180, 120);
                if (patch.Contains(Cursor.Position)) patch.Offset(240, 0);
                Point simulatedPointer = new Point(patch.Right + 10, patch.Bottom + 10);
                bool pointerUnavailable = false;
                using (PassiveWindow background = new PassiveWindow(patch, Color.SteelBlue))
                using (Watcher watcher = new Watcher(delegate { if (pointerUnavailable) throw new PointerUnavailableException(5); return simulatedPointer; }))
                {
                    background.Show(); Pump(200);
                    watcher.Start(patch, 1); Pump(1700);
                    Check(watcher.Covered, "Real timer covers static content");
                    background.BackColor = Color.OrangeRed; background.Refresh(); Pump(350);
                    Check(!watcher.Covered, "Real timer wakes on content change under overlay");
                    Pump(1300); Check(watcher.Covered, "Real timer covers again after new idle period");
                    Screen other = Screen.AllScreens.FirstOrDefault(s => s.DeviceName != screen.DeviceName);
                    if (other != null)
                    {
                        Rectangle otherPatch = new Rectangle(other.Bounds.X + 80, other.Bounds.Y + 80, 180, 120);
                        using (PassiveWindow otherWindow = new PassiveWindow(otherPatch, Color.Purple))
                        {
                            otherWindow.Show(); Pump(350);
                            otherWindow.BackColor = Color.Yellow; otherWindow.Refresh(); Pump(350);
                            Check(watcher.Covered, "Changes on another monitor do not wake target");
                        }
                    }
                    simulatedPointer = new Point(patch.Left + 20, patch.Top + 20);
                    Pump(100); Check(!watcher.Covered, "Mouse poll wakes overlay when pointer enters (injected position)");
                    Pump(1300); Check(!watcher.Covered, "Pointer staying inside keeps monitor awake");
                    simulatedPointer = new Point(patch.Right + 10, patch.Bottom + 10); Pump(1500);
                    int recoveryWarnings = 0; watcher.Failed += delegate { recoveryWarnings++; };
                    Check(watcher.Covered, "Recovery live: test patch is covered before cursor failure");
                    pointerUnavailable = true; Pump(150);
                    Check(watcher.Running && watcher.Waiting && !watcher.Covered && recoveryWarnings == 0, "Recovery live: actual overlay disappears quietly when cursor reading fails");
                    pointerUnavailable = false; PumpUntil(delegate { return !watcher.Waiting; }, 1800);
                    Check(watcher.Running && !watcher.Waiting && !watcher.Covered, "Recovery live: capture and timers resume automatically with a fresh idle baseline");
                    Pump(1400); Check(watcher.Covered, "Recovery live: restored watcher covers again after the full idle interval");
                    watcher.Suspend(true); Pump(100);
                    Check(watcher.Waiting && !watcher.Covered, "Recovery live: simulated session suspension releases the real overlay");
                    watcher.Stop(); Check(!watcher.Covered && !watcher.Running, "Stop removes overlay and releases capture");
                    watcher.Suspend(false); Pump(1200);
                    Check(!watcher.Running && !watcher.Covered, "Recovery live: resume after manual stop does not restart timers");
                }
                MultiMonitorChecks();
                foreach (Screen display in Screen.AllScreens)
                {
                    using (FrameCapture capture = new FrameCapture(display.Bounds))
                    {
                        capture.CaptureChanged();
                        Stopwatch watch = Stopwatch.StartNew();
                        for (int i = 0; i < 12; i++) capture.CaptureChanged();
                        watch.Stop();
                        results.Add("PERF: " + display.DeviceName + " full-resolution capture/compare average " + (watch.Elapsed.TotalMilliseconds / 12).ToString("F1") + " ms");
                    }
                }
                results.Add("Displays: " + string.Join("; ", Screen.AllScreens.Select(s => s.DeviceName + " " + s.Bounds)));
                results.Add("OS: " + Environment.OSVersion);
                File.WriteAllLines(resultPath, results.ToArray());
                return 0;
            }
            catch (Exception e)
            {
                results.Add(e.ToString()); File.WriteAllLines(resultPath, results.ToArray()); return 1;
            }
        }
        internal static int RunLogic(string resultPath)
        {
            results.Clear();
            try { LogicChecks(resultPath); File.WriteAllLines(resultPath, results.ToArray()); return 0; }
            catch (Exception e) { results.Add(e.ToString()); File.WriteAllLines(resultPath, results.ToArray()); return 1; }
        }
        private static void PumpUntil(Func<bool> condition, int timeout)
        {
            Stopwatch wait = Stopwatch.StartNew();
            while (!condition() && wait.ElapsedMilliseconds < timeout) { Application.DoEvents(); Thread.Sleep(10); }
        }
        private static void MultiMonitorChecks()
        {
            Screen[] monitors = Screen.AllScreens.OrderBy(s => s.Bounds.X).ToArray();
            Rectangle a = new Rectangle(monitors[0].Bounds.X + 100, monitors[0].Bounds.Y + 100, 150, 100);
            Rectangle b = monitors.Length > 1 ? new Rectangle(monitors[1].Bounds.X + 100, monitors[1].Bounds.Y + 100, 150, 100) : new Rectangle(a.Right + 100, a.Y, 150, 100);
            if (a.Contains(Cursor.Position)) a.Offset(0, 200);
            if (b.Contains(Cursor.Position)) b.Offset(0, 200);
            Point pointer = new Point(int.MinValue, int.MinValue);
            using (PassiveWindow first = new PassiveWindow(a, Color.CornflowerBlue))
            using (PassiveWindow second = new PassiveWindow(b, Color.SeaGreen))
            using (WatchGroup group = new WatchGroup(delegate { return pointer; }))
            {
                first.Show(); second.Show(); Pump(200);
                group.Start(new[] { new WatchTarget { Name = "A", Bounds = a }, new WatchTarget { Name = "B", Bounds = b } }, 1);
                Pump(1450);
                Check(group.RunningCount == 2 && group.Covered("A") && group.Covered("B"), "Both monitor overlays activate independently at idle timeout");
                first.BackColor = Color.Orange; first.Refresh(); Pump(350);
                Check(!group.Covered("A") && group.Covered("B"), "Content change on A wakes only A while B stays covered");
                Pump(1200); pointer = new Point(a.Left + 20, a.Top + 20); Pump(100);
                Check(!group.Covered("A") && group.Covered("B"), "Pointer entering A wakes only A while B stays covered");
                Pump(1300); Check(!group.Covered("A") && group.Covered("B"), "Pointer remaining in A never prevents idle cover on B");
                pointer = new Point(int.MinValue, int.MinValue); Pump(1300);
                second.BackColor = Color.Purple; second.Refresh(); Pump(350);
                Check(group.Covered("A") && !group.Covered("B"), "Content change on B wakes only B while A stays covered");
                group.Stop(); Check(group.RunningCount == 0 && !group.Covered("A") && !group.Covered("B"), "Group stop releases every monitor overlay and capture");
            }
        }
        private static void LogicChecks(string resultPath)
        {
            RecoveryVerification.Checks(Check);
            IdlePolicy policy = new IdlePolicy(2000, 100);
            Check(!policy.ShouldShade(2099, false), "No cover before timeout");
            Check(policy.ShouldShade(2100, false), "Cover at timeout");
            Check(!policy.ShouldShade(2200, true), "Pointer inside prevents cover");
            policy.Activity(2500);
            Check(!policy.ShouldShade(4400, false), "Change resets countdown");
            Rectangle negative = new Rectangle(-2560, -200, 2560, 1440);
            Check(negative.Contains(-1, 0) && !negative.Contains(0, 0), "Negative monitor coordinates and right boundary");
            string testSettings = Path.Combine(Path.GetDirectoryName(resultPath), "MonitorShade-test-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                Preferences p = new Preferences { DeviceName = "test", WaitValue = 31, Minutes = false };
                p.Write(testSettings); p.WaitValue = 32; p.Write(testSettings);
                Preferences loaded = Preferences.Read(testSettings);
                Check(loaded.DeviceName == "test" && loaded.WaitValue == 32 && !loaded.Minutes, "Settings round trip and atomic replacement");
                Check(loaded.SelectedDevices().SequenceEqual(new[] { "test" }), "Legacy single-monitor settings migrate without losing target");
                p.DeviceNames = new[] { "test", "other", "test" }; p.Write(testSettings);
                Check(Preferences.Read(testSettings).SelectedDevices().SequenceEqual(new[] { "test", "other" }), "Multiple selected monitors persist and duplicate names are removed");
                File.WriteAllText(testSettings, "bad xml");
                Check(Preferences.Read(testSettings).WaitValue == 2, "Corrupt settings recover to defaults");
                p.WaitValue = -5; p.Write(testSettings);
                Check(Preferences.Read(testSettings).WaitValue == 2, "Invalid wait setting recovers to default");
            }
            finally { if (File.Exists(testSettings)) File.Delete(testSettings); }
            IntPtr first = Marshal.AllocHGlobal(8), second = Marshal.AllocHGlobal(8);
            try
            {
                Marshal.WriteInt32(first, 0, 0x123456); Marshal.WriteInt32(first, 4, 0x789ABC);
                Marshal.WriteInt32(second, 0, 0x123456); Marshal.WriteInt32(second, 4, 0x789ABC);
                Check(!FrameCapture.Different(first, second, 2), "Identical RGB pixels compare equal");
                Marshal.WriteInt32(second, 4, 0x789ABD);
                Check(FrameCapture.Different(first, second, 2), "Single-pixel single-channel change is detected");
                Marshal.WriteInt32(second, 4, unchecked((int)0xFF789ABC));
                Check(!FrameCapture.Different(first, second, 2), "Undefined GDI alpha changes are ignored");
            }
            finally { Marshal.FreeHGlobal(first); Marshal.FreeHGlobal(second); }
        }
    }
}
