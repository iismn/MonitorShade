using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace MonitorShade
{
    internal static class RecoveryVerification
    {
        private sealed class FakeSurface : IWatchSurface
        {
            internal bool Disposed, Changed, CaptureFails, CoverFails;
            public bool Covered { get; private set; }
            public event Action WakeRequested;
            public bool CaptureChanged()
            {
                if (CaptureFails) throw new CaptureUnavailableException(new Exception("injected capture failure"));
                bool changed = Changed; Changed = false; return changed;
            }
            public void Cover() { if (CoverFails) throw new InvalidOperationException("injected permanent error"); Covered = true; }
            public void Hide() { Covered = false; }
            public void Dispose() { Covered = false; Disposed = true; }
            internal void MouseWake() { if (WakeRequested != null) WakeRequested(); }
        }
        private sealed class Scenario : IDisposable
        {
            internal long Time;
            internal bool Desktop = true, Pointer = true, FactoryFails;
            internal Point Position = new Point(1000, 1000);
            internal int Created, Failures;
            internal FakeSurface Last;
            internal readonly Watcher Watcher;
            internal Scenario()
            {
                Watcher = new Watcher(delegate { if (!Pointer) throw new PointerUnavailableException(5); return Position; },
                    delegate { return Desktop; }, delegate(Rectangle bounds) {
                        if (FactoryFails) throw new CaptureUnavailableException(new Exception("injected lost desktop DC"));
                        Created++; Last = new FakeSurface(); return Last;
                    }, delegate { return Time; });
                Watcher.Failed += delegate { Failures++; };
            }
            internal void Start() { Watcher.Start(new Rectangle(0, 0, 100, 100), 2); }
            internal void Frame(long time) { Time = time; Watcher.PollFrame(); }
            public void Dispose() { Watcher.Dispose(); }
        }
        internal static void Checks(Action<bool, string> check)
        {
            using (var s = new Scenario())
            {
                s.Start(); s.Frame(1999); check(!s.Watcher.Covered, "Recovery: normal idle interval remains unchanged");
                s.Frame(2000); check(s.Watcher.Covered, "Recovery: stable content is covered at the configured interval");
                FakeSurface old = s.Last; s.Pointer = false; s.Watcher.PollMouse();
                check(s.Watcher.Running && s.Watcher.Waiting && !s.Watcher.Covered && old.Disposed && s.Failures == 0, "Recovery: cursor access failure removes overlay and waits without a warning");
                for (int i = 0; i < 20; i++) s.Frame(3000 + i * 1000);
                check(s.Created == 1 && s.Failures == 0 && s.Watcher.Waiting, "Recovery: prolonged cursor unavailability never captures or floods warnings");
                s.Pointer = true; s.Frame(30000);
                check(s.Created == 2 && !s.Watcher.Waiting && !s.Watcher.Covered, "Recovery: cursor restoration rebuilds capture automatically");
                s.Frame(31999); check(!s.Watcher.Covered, "Recovery: stale idle time cannot immediately cover the restored desktop");
                s.Frame(32000); check(s.Watcher.Covered, "Recovery: full idle interval after restoration still covers normally");
                s.Last.Changed = true; s.Frame(32001); check(!s.Watcher.Covered, "Recovery: restored capture still detects underlying changes");
                s.Frame(35000); s.Last.MouseWake(); check(!s.Watcher.Covered, "Recovery: restored overlay still accepts mouse wake events");
                s.Desktop = false; old = s.Last; s.Frame(36000);
                check(s.Watcher.Waiting && old.Disposed && s.Failures == 0, "Recovery: screen saver or inactive desktop releases capture quietly");
                int created = s.Created; s.Frame(99000); check(s.Created == created, "Recovery: no capture is constructed on an unavailable desktop");
                s.Desktop = true; s.Frame(100000); check(!s.Watcher.Waiting && !s.Watcher.Covered, "Recovery: desktop return restores watching without user input");
                s.Position = new Point(10, 10); s.Frame(200000); check(!s.Watcher.Covered, "Recovery: pointer inside prevents covering after restoration");
                s.Watcher.Suspend(true); s.Frame(210000);
                check(s.Watcher.Waiting && !s.Watcher.Covered, "Recovery: explicit session or power suspension blocks an otherwise available desktop");
                s.Watcher.Suspend(false); s.Frame(211000);
                check(s.Watcher.Running && !s.Watcher.Waiting, "Recovery: system resume restores an active watcher");
                s.Watcher.Suspend(true); s.Watcher.Stop(); created = s.Created;
                s.Watcher.Suspend(false); s.Frame(300000); s.Watcher.PollMouse();
                check(!s.Watcher.Running && !s.Watcher.Waiting && s.Created == created, "Recovery: manual stop cancels pending automatic recovery");
            }
            using (var s = new Scenario())
            {
                s.Desktop = false; s.Start();
                check(s.Watcher.Running && s.Watcher.Waiting && s.Created == 0, "Recovery: automatic startup can wait through an unavailable desktop");
                s.Desktop = true; s.Frame(1000); check(s.Created == 1 && !s.Watcher.Waiting, "Recovery: delayed startup resumes after desktop access returns");
                s.Last.CaptureFails = true; s.Frame(1500);
                check(s.Watcher.Waiting && s.Failures == 0, "Recovery: one capture failure is retried without a warning");
                s.Frame(2500); check(s.Created == 2 && !s.Watcher.Waiting, "Recovery: stale capture resources are recreated successfully");
            }
            using (var s = new Scenario())
            {
                s.FactoryFails = true; s.Start();
                for (int i = 1; i <= 4; i++) s.Frame(i * 1000);
                check(!s.Watcher.Running && s.Failures == 1, "Recovery: repeated capture failures on a normal desktop still report a genuine error");
                s.Frame(10000); check(s.Failures == 1, "Recovery: terminal capture failure is only reported once");
            }
            using (var s = new Scenario())
            {
                s.Start(); s.Last.CoverFails = true; s.Frame(2000);
                check(!s.Watcher.Running && s.Failures == 1 && s.Last.Disposed, "Recovery: unrelated permanent errors remain visible and remove the overlay");
            }
            using (var first = new Scenario()) using (var second = new Scenario())
            {
                first.Start(); second.Start(); first.Frame(2000); second.Frame(2000);
                first.Pointer = false; first.Watcher.PollMouse();
                check(first.Watcher.Waiting && second.Watcher.Covered && second.Watcher.Running, "Recovery: one monitor waiting does not stop another monitor");
                first.Pointer = true; first.Frame(3000); first.Frame(5000);
                check(first.Watcher.Covered && second.Watcher.Covered, "Recovery: recovered monitors continue independent idle tracking");
            }
        }
        internal static int Run(string path)
        {
            var lines = new List<string>();
            try { Checks(delegate(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); lines.Add("PASS: " + message); }); File.WriteAllLines(path, lines.ToArray()); return 0; }
            catch (Exception e) { lines.Add(e.ToString()); File.WriteAllLines(path, lines.ToArray()); return 1; }
        }
    }
}
