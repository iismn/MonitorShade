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

namespace MonitorShade
{
    internal static class WallpaperVerification
    {
        private static readonly List<string> results = new List<string>();
        private static void Check(bool ok, string name)
        { if (!ok) throw new Exception("FAIL: " + name); results.Add("PASS: " + name); }
        private static WindowFootprint Window(int id, params Rectangle[] areas)
        { return new WindowFootprint { Handle = new IntPtr(id), Areas = areas }; }
        internal static int Run(string path, bool live)
        {
            results.Clear();
            try
            {
                Logic(path);
                Pipeline();
                if (live)
                {
                    if (!DesktopAccess.Available())
                    {
                        results.Add("BLOCKED: This process cannot access the interactive input desktop; real capture/overlay tests were not run.");
                        File.WriteAllLines(path, results); return 2;
                    }
                    Live(); RealDesktop();
                }
                results.Add("Wallpaper checks completed."); File.WriteAllLines(path, results); return 0;
            }
            catch (Exception e) { results.Add(e.ToString()); File.WriteAllLines(path, results); return 1; }
        }
        private static void Logic(string path)
        {
            Rectangle bounds = new Rectangle(-12, -8, 12, 10);
            Rectangle left = new Rectangle(-14, -10, 8, 7), right = new Rectangle(-8, -6, 6, 6);
            PixelSpan[] spans = WallpaperMask.Build(bounds, new[] { left, right, new Rectangle(100, 100, 4, 4) });
            var pixels = new HashSet<int>(); bool unique = true;
            foreach (PixelSpan span in spans) for (int p = span.Offset; p < span.Offset + span.Length; p++) unique &= pixels.Add(p);
            Check(unique && pixels.All(p => p >= 0 && p < 120), "Overlapping/clipped windows produce unique in-bounds pixels");
            for (int y = 0; y < bounds.Height; y++) for (int x = 0; x < bounds.Width; x++)
                if (pixels.Contains(y * bounds.Width + x) != (left.Contains(x + bounds.X, y + bounds.Y) || right.Contains(x + bounds.X, y + bounds.Y))) throw new Exception("Pixel geometry mismatch");
            Check(true, "Negative monitor origins and partly offscreen windows preserve exact geometry");
            Check(WallpaperMask.Build(bounds, new Rectangle[0]).Length == 0, "Desktop-only screen has no monitored pixels");
            IntPtr a = Marshal.AllocHGlobal(480), b = Marshal.AllocHGlobal(480);
            try
            {
                for (int p = 0; p < 120; p++) { Marshal.WriteInt32(a, p * 4, 0x102030); Marshal.WriteInt32(b, p * 4, 0x102030); }
                foreach (int p in Enumerable.Range(0, 120).Where(p => !pixels.Contains(p))) Marshal.WriteInt32(b, p * 4, 0xABCDEF);
                Check(!WallpaperMask.Different(a, b, spans), "Animated wallpaper outside small app windows is ignored");
                int first = pixels.First(); Marshal.WriteInt32(b, first * 4, 0x102031);
                Check(WallpaperMask.Different(a, b, spans), "One RGB pixel change inside an app is still detected");
                Marshal.WriteInt32(b, first * 4, unchecked((int)0xFF102030));
                Check(!WallpaperMask.Different(a, b, spans), "Undefined GDI alpha is ignored inside app regions");
                Check(FrameCapture.Different(a, b, 120), "Full-frame mode still sees wallpaper changes");
                Check(!WallpaperMask.Different(a, b, new PixelSpan[0]), "No-app mask ignores all background pixels");
            }
            finally { Marshal.FreeHGlobal(a); Marshal.FreeHGlobal(b); }
            WindowFootprint[] current = new[] { Window(1, Rectangle.Intersect(bounds, left)) };
            var mask = new WallpaperMask(bounds, delegate { return current; });
            Check(mask.Update() && !mask.Update(), "First layout starts a baseline; stable layout does not reset idle");
            current = new[] { Window(2, Rectangle.Intersect(bounds, left)) };
            Check(mask.Update(), "Replacing a window at identical coordinates wakes");
            current = new[] { Window(2, Rectangle.Intersect(bounds, right)) };
            Check(mask.Update(), "Moving/resizing a window changes the mask");
            current = new[] { Window(2, Rectangle.Intersect(bounds, right)), Window(3, Rectangle.Intersect(bounds, left)) };
            Check(mask.Update(), "A newly opened window wakes");
            current = current.Reverse().ToArray(); Check(mask.Update(), "App Z-order changes wake even when union geometry is identical");
            current = new WindowFootprint[0]; Check(mask.Update() && !mask.Update(), "Closing last app wakes once then allows desktop-only sleep");
            current = null; bool failed = false;
            try { mask.Update(); } catch (Win32Exception) { failed = true; }
            Check(failed, "Unavailable enumeration is never accepted as an empty desktop");
            foreach (string name in new[] { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" }) Check(DesktopWindows.IsDesktop(name), "Exclude shell background: " + name);
            foreach (string name in new[] { "Chrome_WidgetWin_1", "ApplicationFrameWindow", "Notepad", "#32768" }) Check(!DesktopWindows.IsDesktop(name), "Keep app/video/menu window: " + name);
            string settings = Path.Combine(Path.GetDirectoryName(path), "wallpaper-settings-test.xml");
            try
            {
                File.WriteAllText(settings, "<Preferences><WaitValue>17</WaitValue><Minutes>false</Minutes><CenterPercent>72</CenterPercent></Preferences>");
                Preferences p = Preferences.Read(settings);
                Check(p.IgnoreWallpaper && p.WaitValue == 17 && !p.Minutes && p.CenterPercent == 72, "Old preferences enable wallpaper exclusion and preserve existing values");
                p.IgnoreWallpaper = false; p.Write(settings);
                Check(!Preferences.Read(settings).IgnoreWallpaper, "Disabled option survives restart");
                p.IgnoreWallpaper = true; p.Write(settings);
                Check(Preferences.Read(settings).IgnoreWallpaper, "Enabled option survives restart");
            }
            finally { if (File.Exists(settings)) File.Delete(settings); }
        }
        private static void Pump(int milliseconds, Action animate)
        {
            Stopwatch time = Stopwatch.StartNew();
            while (time.ElapsedMilliseconds < milliseconds)
            { if (animate != null) animate(); Application.DoEvents(); Thread.Sleep(35); }
            Native.DwmFlush();
        }
        private sealed class MemorySurface : IWatchSurface
        {
            internal readonly int[] Pixels = new int[64];
            internal WindowFootprint[] Windows = new[] { Window(1, new Rectangle(2, 2, 3, 3)) };
            private readonly WallpaperMask mask;
            private readonly IntPtr before = Marshal.AllocHGlobal(256), after = Marshal.AllocHGlobal(256);
            public event Action WakeRequested { add { } remove { } }
            public bool Covered { get; private set; }
            internal MemorySurface()
            {
                mask = new WallpaperMask(new Rectangle(0, 0, 8, 8), delegate { return Windows; });
                mask.Update(); Marshal.Copy(Pixels, 0, before, 64);
            }
            public bool CaptureChanged()
            {
                bool layout = mask.Update(); Marshal.Copy(Pixels, 0, after, 64);
                bool pixels = WallpaperMask.Different(before, after, mask.Spans);
                Marshal.Copy(Pixels, 0, before, 64); return layout || pixels;
            }
            public void Cover() { Covered = true; }
            public void Hide() { Covered = false; }
            public void Dispose() { Marshal.FreeHGlobal(before); Marshal.FreeHGlobal(after); }
        }
        private static void Pipeline()
        {
            long now = 0; Point pointer = new Point(20, 20);
            MemorySurface first = null, second = null;
            using (var watcher = new Watcher(delegate { return pointer; }, delegate { return true; }, delegate { return first = new MemorySurface(); }, delegate { return now; }))
            using (var other = new Watcher(delegate { return pointer; }, delegate { return true; }, delegate { return second = new MemorySurface(); }, delegate { return now; }))
            {
                watcher.Start(new Rectangle(0, 0, 8, 8), 1); other.Start(new Rectangle(100, 0, 8, 8), 1);
                now = 800; first.Pixels[0] = 7; watcher.PollFrame();
                now = 1000; first.Pixels[1] = 9; watcher.PollFrame(); other.PollFrame();
                Check(watcher.Covered && other.Covered, "Watchers shade independently while exposed background changes");
                now = 1200; first.Pixels[18] = 1; watcher.PollFrame(); other.PollFrame();
                Check(!watcher.Covered && other.Covered, "App pixel wakes only its own monitor");
                for (int i = 0; i < 12; i++) { now += 250; first.Pixels[18]++; watcher.PollFrame(); }
                Check(!watcher.Covered, "Continuous app video prevents shading");
                now += 1000; watcher.PollFrame(); Check(watcher.Covered, "App becomes idle for a full interval before shading");
                first.Windows = new[] { Window(1, new Rectangle(3, 2, 3, 3)) }; watcher.PollFrame();
                Check(!watcher.Covered, "Mask geometry change wakes real watcher policy");
                now += 1000; watcher.PollFrame(); first.Windows = new[] { Window(2, new Rectangle(3, 2, 3, 3)) }; watcher.PollFrame();
                Check(!watcher.Covered, "Replacement window with same pixels and geometry wakes");
                now += 1000; watcher.PollFrame(); first.Windows = new WindowFootprint[0]; watcher.PollFrame();
                Check(!watcher.Covered, "Last window closing restarts the idle interval");
                now += 999; first.Pixels[30]++; watcher.PollFrame(); Check(!watcher.Covered, "Empty desktop still observes configured delay");
                now++; watcher.PollFrame(); Check(watcher.Covered, "Wallpaper-only monitor reaches idle despite pixel changes");
                first.Pixels[12]++; watcher.PollFrame(); Check(watcher.Covered, "Wallpaper changes alone keep the overlay closed");
                pointer = new Point(1, 1); watcher.PollMouse(); Check(!watcher.Covered, "Pointer entry wakes without waiting for frame polling");
                now += 10000; watcher.PollFrame(); Check(!watcher.Covered && other.Covered, "Pointer blocks shading only on its own monitor");
            }
        }
        private static void Live()
        {
            foreach (Screen display in Screen.AllScreens)
            {
                string prefix = display.DeviceName + " ";
                Rectangle area = new Rectangle(display.Bounds.X + 80, display.Bounds.Y + 140, 340, 230);
                Point pointer = new Point(area.Right + 50, area.Bottom + 50);
                var clock = Stopwatch.StartNew();
                using (var background = new PassiveWindow(area, Color.Purple))
                using (var app = new PassiveWindow(new Rectangle(area.X + 70, area.Y + 60, 110, 90), Color.SteelBlue))
                using (var popup = new PassiveWindow(new Rectangle(area.X + 200, area.Y + 50, 70, 65), Color.Goldenrod))
                {
                    background.Show(); app.Show(); Pump(250, null);
                    var allowed = new HashSet<IntPtr> { app.Handle, popup.Handle };
                    Func<Rectangle, WindowFootprint[]> read = delegate(Rectangle r) { return DesktopWindows.Read(r).Where(w => allowed.Contains(w.Handle)).ToArray(); };
                    var mask = new WallpaperMask(area, read);
                    bool alternate = false;
                    Action animate = delegate { alternate = !alternate; background.BackColor = alternate ? Color.Purple : Color.DarkGreen; background.Refresh(); };
                    using (var watcher = new Watcher(delegate { return pointer; }, DesktopAccess.Available, delegate(Rectangle r) { return new WatchSurface(r, mask); }, delegate { return clock.ElapsedMilliseconds; }))
                    {
                        int errors = 0; watcher.Failed += delegate(string message) { results.Add(message); errors++; };
                        watcher.Start(area, 1); Pump(1550, animate);
                        Check(watcher.Covered, prefix + "Animated exposed background does not prevent shading around a static app");
                        Pump(500, animate); Check(watcher.Covered, prefix + "Background animation does not wake the overlay");
                        app.BackColor = Color.OrangeRed; app.Refresh(); Pump(400, animate);
                        Check(!watcher.Covered, prefix + "Real app content wakes through a capture-excluded overlay");
                        Pump(1400, animate); Check(watcher.Covered, prefix + "Static app shades again");
                        app.Left += 12; Pump(400, animate); Check(!watcher.Covered, prefix + "Moving app wakes and refreshes mask");
                        Pump(1400, animate); popup.Show(); Pump(400, animate);
                        Check(!watcher.Covered, prefix + "New popup wakes");
                        Pump(1400, animate); Check(watcher.Covered, prefix + "Popup and app stay asleep despite background motion");
                        popup.Hide(); app.Hide(); Pump(400, animate); Check(!watcher.Covered, prefix + "Closing last app wakes once");
                        Pump(1400, animate); Check(watcher.Covered, prefix + "Wallpaper-only screen shades");
                        pointer = new Point(area.X + 10, area.Y + 10); watcher.PollMouse();
                        Check(!watcher.Covered, prefix + "Mouse entry clears wallpaper-only overlay immediately");
                        Check(errors == 0 && watcher.Running, prefix + "Watcher remains healthy");
                    }
                    using (var full = new Watcher(delegate { return pointer; }))
                    {
                        pointer = new Point(area.Right + 50, area.Bottom + 50);
                        full.Start(area, 1); Pump(1500, animate);
                        Check(!full.Covered, prefix + "Option off retains whole-screen motion detection");
                    }
                }
            }
        }
        private static void RealDesktop()
        {
            // Read actual shell geometry; no existing window, mouse or wallpaper settings are changed.
            foreach (Screen screen in Screen.AllScreens)
            {
                WindowFootprint[] windows = DesktopWindows.Read(screen.Bounds);
                results.Add("INFO: " + screen.DeviceName + " app windows=" + windows.Length + ", monitored pixels=" + WallpaperMask.Build(screen.Bounds, windows.SelectMany(w => w.Areas)).Sum(s => (long)s.Length));
                Rectangle free = Rectangle.Empty;
                for (int y = screen.WorkingArea.Top + 40; y + 230 < screen.WorkingArea.Bottom && free.IsEmpty; y += 120)
                    for (int x = screen.WorkingArea.Left + 40; x + 340 < screen.WorkingArea.Right; x += 160)
                    {
                        Rectangle candidate = new Rectangle(x, y, 340, 230);
                        if (!windows.Any(w => w.Areas.Any(a => a.IntersectsWith(candidate)))) { free = candidate; break; }
                    }
                if (free.IsEmpty) { results.Add("SKIP: " + screen.DeviceName + " no unobstructed desktop patch available for Lively test"); continue; }
                using (var app = new PassiveWindow(new Rectangle(free.X + 80, free.Y + 60, 120, 100), Color.Teal))
                using (var raw = new FrameCapture(free))
                {
                    app.Show(); Pump(300, null);
                    var mask = new WallpaperMask(free); mask.Update();
                    Check(mask.Spans.Sum(s => s.Length) > 0 && mask.Spans.Sum(s => s.Length) < free.Width * free.Height / 2, screen.DeviceName + " Actual desktop mask keeps small app and excludes exposed wallpaper");
                    using (var surface = new WatchSurface(free, mask))
                    {
                        raw.CaptureChanged(); surface.CaptureChanged();
                        bool rawChanged = false, appChanged = false;
                        for (int i = 0; i < 8; i++) { Pump(250, null); rawChanged |= raw.CaptureChanged(); appChanged |= surface.CaptureChanged(); }
                        Check(!appChanged, screen.DeviceName + " Actual wallpaper around a fixed app does not count as activity");
                        results.Add((rawChanged ? "PASS: " : "INFO: ") + screen.DeviceName + (rawChanged ? " Real desktop animation observed and excluded" : " Desktop animation was paused/static during sample"));
                        surface.Cover(); Pump(300, null); Check(!surface.CaptureChanged(), screen.DeviceName + " Actual shell mask excludes MonitorShade overlay itself");
                        app.BackColor = Color.Crimson; app.Refresh(); Pump(300, null);
                        Check(surface.CaptureChanged(), screen.DeviceName + " App change detected over real desktop through black overlay");
                    }
                }
            }
        }
    }
}
