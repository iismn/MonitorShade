using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace MonitorShade
{
    internal static class ManualShadeVerification
    {
        private sealed class FakeCover : IManualCover
        {
            internal bool Visible, Disposed, Fail;
            public void Cover() { if (Fail) throw new Exception("Test failure"); Visible = true; }
            public void Dispose() { Disposed = true; Visible = false; }
        }
        internal static int Run(string path, bool live)
        {
            var lines = new List<string>();
            Action<bool, string> check = delegate(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); lines.Add("PASS: " + name); };
            try
            {
                bool available = true, failPointer = false, failCover = false; Point pointer = new Point(400, 400);
                Rectangle a = new Rectangle(-100, -20, 100, 80), b = new Rectangle(0, 0, 100, 80);
                var created = new List<FakeCover>(); var held = new HashSet<string>();
                using (var controller = new ManualShadeController(delegate { return available; }, delegate { if (failPointer) throw new PointerUnavailableException(5); return pointer; }, delegate { var c = new FakeCover { Fail = failCover }; created.Add(c); return c; }, delegate(string name, bool hold) { if (hold) held.Add(name); else held.Remove(name); }))
                {
                    controller.Poll(); check(controller.Count == 0 && created.Count == 0, "Manual controller never shades by itself");
                    controller.Toggle("A", a); check(controller.Count == 1 && created.Last().Visible && held.SetEquals(new[] { "A" }), "Shortcut covers one target and holds only that automatic watcher");
                    controller.Poll(); check(controller.Count == 1, "Staying on another monitor does not release forced shade");
                    pointer = new Point(-50, 0); controller.Poll(); check(controller.Count == 0 && created.Last().Disposed && held.Count == 0, "Pointer enters negative-coordinate monitor and releases its cover/hold");
                    controller.Toggle("A", a); controller.Poll(); check(controller.Count == 1, "Activation while pointer already inside does not immediately cancel");
                    pointer = new Point(110, 0); controller.Poll(); check(controller.Count == 1, "Leaving initially covered target arms entry detection");
                    pointer = new Point(-1, 0); controller.Poll(); check(controller.Count == 0, "Returning after leaving releases cover");
                    pointer = new Point(500, 500); controller.Toggle("A", a); controller.Toggle("A", a);
                    check(controller.Count == 0 && held.Count == 0, "Repeating same shortcut toggles off without moving pointer");
                    controller.Toggle("A", a); controller.Toggle("B", b); check(controller.Count == 2 && held.Count == 2, "Both force shortcuts can cover independently");
                    pointer = new Point(0, 0); controller.Poll(); check(controller.Count == 1 && held.SetEquals(new[] { "A" }), "Entry wakes only B at shared right-exclusive boundary");
                    controller.Clear(); check(controller.Count == 0 && held.Count == 0 && created.All(c => c.Disposed), "Settings or pause releases every cover and hold");
                    pointer = new Point(500, 500); controller.Toggle("A", a); available = false; controller.Poll();
                    check(controller.Count == 0 && held.Count == 0, "Desktop access interruption clears force mode");
                    controller.Toggle("A", a); check(controller.Count == 0, "Blocked desktop cannot queue a delayed force cover");
                    available = true; controller.Poll(); check(controller.Count == 0, "Unlock does not restore a previous force cover");
                    failPointer = true; controller.Toggle("A", a); check(controller.Count == 0, "Pointer read failure on activation leaves screen visible");
                    failPointer = false; controller.Toggle("A", a); failPointer = true; controller.Poll(); check(controller.Count == 0 && held.Count == 0, "Pointer read failure while covered releases quietly");
                    failPointer = false; failCover = true; bool failed = false;
                    try { controller.Toggle("A", a); } catch { failed = true; }
                    check(failed && controller.Count == 0 && created.Last().Disposed && held.Count == 0, "Failed overlay creation rolls back automatic hold and resource");
                }
                using (var group = new WatchGroup())
                {
                    group.Hold("A", true); group.Start(new[] { new WatchTarget { Name = "A", Bounds = a } }, 5);
                    check(group.Held("A") && group.RunningCount == 1 && group.WaitingCount == 1, "Starting automatic watch under a manual cover creates no capture");
                    group.Suspend(false); check(group.Held("A") && group.WaitingCount == 1, "System resume cannot override a manual hold");
                    group.Stop(); check(group.Held("A"), "Reconfiguring automatic watch preserves active manual hold");
                    group.Hold("A", false); check(!group.Held("A"), "Manual release clears hold for the next automatic start");
                }
                string temporary = Path.Combine(Path.GetDirectoryName(path), "manual-settings-test.xml");
                try
                {
                    File.WriteAllText(temporary, "<Preferences><WaitValue>60</WaitValue><Minutes>false</Minutes></Preferences>");
                    var preferences = Preferences.Read(temporary);
                    check(preferences.ManualShade.Enabled && preferences.ManualShade.FirstDevice == null, "Legacy settings get force shortcut defaults without losing delay");
                    preferences.ManualShade.FirstDevice = ""; preferences.ManualShade.SecondDevice = "missing-target"; preferences.ManualShade.Enabled = false; preferences.Write(temporary);
                    var read = Preferences.Read(temporary);
                    check(!read.ManualShade.Enabled && read.ManualShade.FirstDevice == "" && read.ManualShade.SecondDevice == "missing-target" && read.WaitValue == 60, "Disabled shortcut targets and absent monitor identity survive restart");
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                if (live)
                {
                    if (!DesktopAccess.Available()) { lines.Add("BLOCKED: Interactive desktop unavailable; live overlays not tested."); File.WriteAllLines(path, lines); return 2; }
                    foreach (Screen screen in Screen.AllScreens)
                    {
                        Rectangle patch = new Rectangle(screen.Bounds.X + 80, screen.Bounds.Y + 100, 180, 120);
                        Point pointerNow = new Point(patch.Right + 20, patch.Bottom + 20);
                        using (var controller = new ManualShadeController(DesktopAccess.Available, delegate { return pointerNow; }, delegate(Rectangle area) { return new ManualCover(area); }, delegate { }))
                        {
                            IntPtr focus = Native.GetForegroundWindow();
                            controller.Toggle(screen.DeviceName, patch); Pump(250);
                            check(controller.Count == 1 && Application.OpenForms.Cast<Form>().OfType<ShadeWindow>().Any(f => f.Visible && f.Bounds == patch), screen.DeviceName + " real black cover appears at target coordinates");
                            check(Native.GetForegroundWindow() == focus, screen.DeviceName + " force overlay does not steal focus");
                            pointerNow = new Point(patch.X + 1, patch.Y + 1); controller.Poll(); Pump(100);
                            check(controller.Count == 0 && !Application.OpenForms.Cast<Form>().OfType<ShadeWindow>().Any(f => f.Visible && f.Bounds == patch), screen.DeviceName + " real overlay disposed on pointer entry");
                        }
                    }
                }
                File.WriteAllLines(path, lines); return 0;
            }
            catch (Exception e) { lines.Add(e.ToString()); File.WriteAllLines(path, lines); return 1; }
        }
        private static void Pump(int duration)
        { var clock = Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < duration) { Application.DoEvents(); Thread.Sleep(10); } }
    }
}
