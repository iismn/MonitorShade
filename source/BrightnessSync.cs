using System;
using System.Collections.Generic;
using System.Linq;

namespace MonitorShade
{
    internal static class BrightnessSync
    {
        internal static DdcChange[] Step(DdcMonitor[] targets, IDictionary<string, int> desired, bool linked, int delta)
        {
            if (targets.Length == 0) return new DdcChange[0];
            Func<DdcMonitor, int> value = delegate(DdcMonitor m) { int v; return desired.TryGetValue(m.Id, out v) ? v : m.Percent; };
            // Raising must not lower the brighter display; lowering must not raise the dimmer one.
            int common = Clamp((delta >= 0 ? targets.Max(value) : targets.Min(value)) + delta);
            return targets.Select(m => new DdcChange { Id = m.Id, Percent = linked ? common : Clamp(value(m) + delta) }).ToArray();
        }
        internal static int Clamp(int value) { return Math.Max(0, Math.Min(100, value)); }
    }
}
