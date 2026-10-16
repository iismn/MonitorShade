using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace MonitorShade
{
    internal static class DisplayRecoveryVerification
    {
        private sealed class Surface : IWatchSurface
        {
            internal bool Disposed;
            public bool Covered { get; private set; }
            public event Action WakeRequested { add { } remove { } }
            public bool CaptureChanged() { return false; }
            public void Cover() { Covered = true; }
            public void Hide() { Covered = false; }
            public void Dispose() { Disposed = true; Covered = false; }
        }
        private sealed class Scenario : IDisposable
        {
            internal long Time;
            internal bool Desktop = true, FailNext;
            internal readonly List<Surface> Surfaces = new List<Surface>();
            internal readonly List<Watcher> Watchers = new List<Watcher>();
            internal readonly WatchGroup Group;
            internal readonly DisplayRecovery Recovery;
            internal int Failures;
            internal Scenario()
            {
                Group = new WatchGroup(delegate(bool ignore)
                {
                    var watcher = new Watcher(delegate { return new Point(9999, 9999); }, delegate { return Desktop; },
                        delegate(Rectangle bounds)
                        {
                            if (FailNext) { FailNext = false; throw new InvalidOperationException("injected create failure"); }
                            var surface = new Surface(); Surfaces.Add(surface); return surface;
                        }, delegate { return Time; });
                    Watchers.Add(watcher); return watcher;
                });
                Group.Failed += delegate { Failures++; };
                Recovery = new DisplayRecovery(Group);
            }
            internal void Frame(long time) { Time = time; foreach (Watcher watcher in Watchers) watcher.PollFrame(); }
            public void Dispose() { Recovery.Dispose(); Group.Dispose(); }
        }
        private static ConnectedDisplay Display(string name, string id, int x)
        { return new ConnectedDisplay { DeviceName = name, MonitorId = id, Bounds = new Rectangle(x, 0, 100, 100) }; }
        internal static int Run(string path)
        {
            var lines = new List<string>();
            Action<bool, string> check = delegate(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); lines.Add("PASS: " + name); };
            string settings = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), "reconnect-test.settings.xml");
            try
            {
                ConnectedDisplay left = Display("DISPLAY5", "physical-left", -100), right = Display("DISPLAY1", "physical-right", 0);
                var both = new[] { left, right };
                var preferences = new Preferences { DeviceNames = new[] { left.DeviceName, right.DeviceName }, WaitValue = 30, Minutes = false };
                var selection = new DisplaySelection(preferences);
                check(selection.Resolve(both).Length == 2, "Legacy settings retain both selected monitors");
                selection.Store(preferences);
                check(preferences.WatchDisplays.All(d => !string.IsNullOrEmpty(d.MonitorId)), "Legacy names migrate to monitor interface identities");
                selection.Resolve(new[] { right }); selection.Store(preferences); preferences.Write(settings);
                var restored = new DisplaySelection(Preferences.Read(settings));
                check(restored.Count == 2 && restored.Resolve(new[] { right }).Length == 1, "Saving while unplugged preserves the missing monitor across restart");
                ConnectedDisplay renamed = Display("DISPLAY8", "physical-left", -100);
                check(restored.Resolve(new[] { renamed, right }).Any(d => d.DeviceName == "DISPLAY8"), "Reconnect follows physical monitor when Windows display number changes");
                restored.Store(preferences);
                check(preferences.DeviceNames.Contains("DISPLAY8"), "Resolved display alias is updated for subsequent launches");
                ConnectedDisplay other = Display("DISPLAY8", "different-monitor", -100);
                check(restored.Resolve(new[] { other, right }).Length == 1, "A different monitor reusing a saved display number is not covered");
                check(restored.Resolve(new[] { Display("DISPLAY8", "", -100), right }).Length == 1, "Incomplete identity enumeration waits instead of guessing a target");
                restored.Edit(new[] { right }, new string[0]);
                check(restored.Count == 1 && restored.Resolve(new[] { renamed, right }).Single().MonitorId == "physical-left", "Deselecting a connected monitor keeps an unplugged selection");
                restored.Edit(new[] { renamed, right }, new string[0]);
                check(restored.Count == 0, "Explicitly deselected monitors remain excluded on reconnect");
                using (var s = new Scenario())
                {
                    s.Recovery.Start(2, true); s.Recovery.Refresh(both); s.Frame(2000);
                    Surface originalLeft = s.Surfaces[0], originalRight = s.Surfaces[1];
                    check(s.Group.RunningCount == 2 && s.Group.Covered(left.DeviceName) && s.Group.Covered(right.DeviceName), "Both monitors independently shade at the configured delay");
                    s.Recovery.Refresh(new[] { right });
                    check(s.Recovery.Enabled && s.Group.RunningCount == 1 && originalLeft.Disposed && !originalRight.Disposed && s.Group.Covered(right.DeviceName), "Unplugging one removes only its overlay and retains the other watcher");
                    s.Time = 3000; s.Recovery.Refresh(both);
                    check(s.Group.RunningCount == 2 && !s.Group.Covered(left.DeviceName) && s.Group.Covered(right.DeviceName), "Reconnected target resumes automatically without resetting the unchanged target");
                    s.Frame(4999); check(!s.Group.Covered(left.DeviceName), "Reconnected target receives a full fresh idle interval");
                    s.Frame(5000); check(s.Group.Covered(left.DeviceName), "Reconnected target shades after its fresh delay");
                    int created = s.Surfaces.Count;
                    for (int i = 0; i < 30; i++) s.Recovery.Refresh(both);
                    check(s.Surfaces.Count == created && s.Group.Covered(left.DeviceName), "Repeated topology notifications and polling do not reset healthy watchers");
                    s.Recovery.Refresh(new ConnectedDisplay[0]);
                    check(s.Recovery.Enabled && !s.Group.Running && s.Surfaces.All(surface => surface.Disposed), "Disconnecting every target keeps reconnect intent and frees all overlays");
                    s.Time = 6000; s.Recovery.Refresh(new[] { renamed, right });
                    check(s.Group.RunningCount == 2 && !s.Group.Covered(renamed.DeviceName), "All monitors return without a manual restart");
                    s.Frame(8000);
                    created = s.Surfaces.Count; s.Time = 9000;
                    ConnectedDisplay rotated = Display(renamed.DeviceName, renamed.MonitorId, -200); rotated.Bounds = new Rectangle(-200, -30, 80, 120);
                    s.Recovery.Refresh(new[] { rotated, right });
                    check(s.Surfaces.Count == created + 1 && !s.Group.Covered(rotated.DeviceName) && s.Group.Covered(right.DeviceName), "Resolution, rotation or position changes rebuild only the affected target");
                    s.Recovery.Stop(); created = s.Surfaces.Count;
                    s.Recovery.Refresh(both); s.Frame(20000);
                    check(!s.Recovery.Enabled && !s.Group.Running && created == s.Surfaces.Count, "Manual pause prevents topology events from restarting protection");
                }
                using (var s = new Scenario())
                {
                    s.Recovery.Start(30, true); s.Recovery.Refresh(new ConnectedDisplay[0]);
                    s.Frame(120000); check(s.Recovery.Enabled && s.Surfaces.Count == 0, "Startup can wait indefinitely for missing selected monitors");
                    s.Recovery.Refresh(new[] { right });
                    check(s.Group.RunningCount == 1, "A present monitor starts while another saved monitor is absent");
                    s.Group.Suspend(true); s.Time = 121000; s.Recovery.Refresh(both);
                    check(s.Group.WaitingCount == 2 && s.Surfaces.Count == 1, "Reconnect during a locked or sleeping session never creates a visible overlay");
                    s.Group.Suspend(false); s.Frame(122000);
                    check(s.Group.WaitingCount == 0 && s.Group.RunningCount == 2, "Unlock resumes both existing and newly reconnected watchers");
                    s.Frame(151999); check(!s.Group.Covered(left.DeviceName), "Unlock also restarts the configured idle delay");
                    s.Frame(152000); check(s.Group.Covered(left.DeviceName), "Recovered target shades normally after unlock");
                }
                using (var s = new Scenario())
                {
                    s.FailNext = true; s.Recovery.Start(2, false); s.Recovery.Refresh(both);
                    check(s.Failures == 1 && s.Group.RunningCount == 1, "One target failing to initialize does not stop another monitor");
                    for (int i = 0; i < 10; i++) s.Recovery.Refresh(both);
                    check(s.Failures == 1, "Permanent per-monitor failure is not retried or notified every poll");
                    s.Recovery.Refresh(new[] { right }); s.Recovery.Refresh(both);
                    check(s.Group.RunningCount == 2, "A failed target receives a fresh attempt after unplug and reconnect");
                }
                lines.Add("Reconnect tests use simulated display changes; no physical monitor settings were changed.");
                File.WriteAllLines(path, lines.ToArray()); return 0;
            }
            catch (Exception e) { lines.Add(e.ToString()); File.WriteAllLines(path, lines.ToArray()); return 1; }
            finally { if (File.Exists(settings)) File.Delete(settings); }
        }
    }
}
