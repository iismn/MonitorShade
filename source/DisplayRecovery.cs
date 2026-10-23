using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonitorShade
{
    public sealed class RememberedDisplay
    {
        public string DeviceName = "";
        public string MonitorId = "";
        internal RememberedDisplay Copy() { return new RememberedDisplay { DeviceName = DeviceName, MonitorId = MonitorId }; }
    }

    internal sealed class ConnectedDisplay
    {
        internal string DeviceName, MonitorId;
        internal Rectangle Bounds;
        internal bool Primary;
        internal static ConnectedDisplay[] Read()
        {
            // Screen.AllScreens caches its fallback display if enumeration happens during a
            // screen saver or desktop transition. Read the live topology on every retry instead.
            var connected = new List<ConnectedDisplay>();
            bool complete = DdcNative.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr monitor, IntPtr dc, ref DdcNative.RECT bounds, IntPtr context)
            {
                var info = new DdcNative.MONITORINFO { Size = Marshal.SizeOf(typeof(DdcNative.MONITORINFO)) };
                if (!DdcNative.GetMonitorInfo(monitor, ref info)) return false;
                var device = new DdcNative.DISPLAY_DEVICE { Size = Marshal.SizeOf(typeof(DdcNative.DISPLAY_DEVICE)) };
                string id = DdcNative.EnumDisplayDevices(info.Device, 0, ref device, 1) ? device.Id : "";
                connected.Add(new ConnectedDisplay { DeviceName = info.Device, MonitorId = id ?? "", Primary = (info.Flags & 1) != 0,
                    Bounds = Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom) });
                return true;
            }, IntPtr.Zero);
            return complete ? connected.ToArray() : new ConnectedDisplay[0];
        }
    }

    // Keep the user's intent separately from whichever displays Windows currently exposes.
    // A saved interface identity takes precedence over the reusable DISPLAY1/2/... alias.
    internal sealed class DisplaySelection
    {
        private readonly List<RememberedDisplay> selected;
        internal int Revision { get; private set; }
        internal int Count { get { return selected.Count; } }
        internal DisplaySelection(Preferences preferences)
        {
            selected = (preferences.WatchDisplays ?? new RememberedDisplay[0])
                .Where(d => d != null && !string.IsNullOrWhiteSpace(d.DeviceName)).Select(d => d.Copy()).ToList();
            if (selected.Count == 0)
                selected.AddRange(preferences.SelectedDevices().Select(n => new RememberedDisplay { DeviceName = n }));
        }
        private static bool Same(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        private static ConnectedDisplay Match(RememberedDisplay saved, ConnectedDisplay[] connected)
        {
            return string.IsNullOrWhiteSpace(saved.MonitorId)
                ? connected.FirstOrDefault(d => Same(saved.DeviceName, d.DeviceName))
                : connected.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.MonitorId) && Same(saved.MonitorId, d.MonitorId));
        }
        internal ConnectedDisplay[] Resolve(ConnectedDisplay[] connected)
        {
            var result = new List<ConnectedDisplay>();
            foreach (RememberedDisplay saved in selected)
            {
                ConnectedDisplay match = Match(saved, connected);
                if (match == null) continue;
                if (!Same(saved.DeviceName, match.DeviceName) || !Same(saved.MonitorId, match.MonitorId))
                { saved.DeviceName = match.DeviceName; saved.MonitorId = match.MonitorId; Revision++; }
                if (!result.Any(d => Same(d.DeviceName, match.DeviceName))) result.Add(match);
            }
            return result.ToArray();
        }
        internal void Edit(ConnectedDisplay[] connected, string[] selectedNames)
        {
            // Editing a connected monitor must not forget another selected monitor while it is unplugged.
            Resolve(connected);
            selected.RemoveAll(d => Match(d, connected) != null);
            foreach (ConnectedDisplay display in connected.Where(d => selectedNames.Any(n => Same(n, d.DeviceName))))
                selected.Add(new RememberedDisplay { DeviceName = display.DeviceName, MonitorId = display.MonitorId });
            Revision++;
        }
        internal void Store(Preferences preferences)
        {
            preferences.WatchDisplays = selected.Select(d => d.Copy()).ToArray();
            preferences.DeviceNames = selected.Select(d => d.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            preferences.DeviceName = preferences.DeviceNames.FirstOrDefault() ?? "";
        }
    }

    internal sealed class DisplayRecovery : IDisposable
    {
        private readonly WatchGroup watcher;
        private int seconds;
        private bool ignoreWallpaper;
        internal bool Enabled { get; private set; }
        internal DisplayRecovery(WatchGroup group) { watcher = group; }
        internal void Start(int delay, bool ignore)
        { watcher.Stop(); Configure(delay, ignore); }
        internal void Configure(int delay, bool ignore)
        {
            if (delay < 1 || delay > 86400) throw new ArgumentOutOfRangeException("delay");
            seconds = delay; ignoreWallpaper = ignore; Enabled = true;
        }
        internal void Refresh(ConnectedDisplay[] targets)
        {
            if (!Enabled) return;
            watcher.Reconcile(targets.Select(d => new WatchTarget { Name = d.DeviceName, Identity = d.MonitorId, Bounds = d.Bounds }), seconds, ignoreWallpaper);
        }
        internal void Stop() { Enabled = false; watcher.Stop(); }
        public void Dispose() { Stop(); }
    }
}
