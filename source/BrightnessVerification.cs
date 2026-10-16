using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MonitorShade
{
    internal static class BrightnessVerification
    {
        internal static int Run(string path)
        {
            var lines = new List<string>();
            Action<bool, string> check = delegate(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); lines.Add("PASS: " + message); };
            try
            {
                foreach (uint maximum in new uint[] { 100, 255, 65535 })
                    foreach (int percent in new[] { 0, 1, 50, 99, 100 }) check(Math.Abs(DdcMath.Percent(DdcMath.Raw(percent, maximum), maximum) - percent) <= 1, "Brightness range round trip: " + percent + "/" + maximum);
                check(DdcMath.Raw(-1, 100) == 0 && DdcMath.Raw(101, 100) == 100, "Brightness writes clamp to advertised range");
                check(!DdcMath.Valid(0, 0) && !DdcMath.Valid(101, 100) && !DdcMath.Valid(1, uint.MaxValue), "Reject malformed monitor values before writes");
                var targets = new[] { new DdcMonitor { Id = "A", Current = 40, Maximum = 100 }, new DdcMonitor { Id = "B", Current = 60, Maximum = 100 } };
                var queued = new Dictionary<string, int>();
                check(BrightnessSync.Step(targets, queued, true, 5).All(c => c.Percent == 65), "40/60 increase synchronizes to 65/65");
                check(BrightnessSync.Step(targets, queued, true, -5).All(c => c.Percent == 35), "40/60 decrease synchronizes to 35/35 without brightening either monitor");
                check(BrightnessSync.Step(targets, queued, false, 5).Select(c => c.Percent).SequenceEqual(new[] { 45, 65 }), "Disabled synchronization preserves independent steps");
                queued["A"] = queued["B"] = 65;
                check(BrightnessSync.Step(targets, queued, true, 5).All(c => c.Percent == 70), "Rapid repeat builds on pending synchronized target");
                check(BrightnessSync.Step(targets, queued, true, -5).All(c => c.Percent == 60), "Reverse direction while pending remains synchronized");
                queued["A"] = 100; queued["B"] = 95;
                check(BrightnessSync.Step(targets, queued, true, 5).All(c => c.Percent == 100), "Linked increase clamps both monitors at 100 percent");
                queued["A"] = 0; queued["B"] = 5;
                check(BrightnessSync.Step(targets, queued, true, -5).All(c => c.Percent == 0), "Linked decrease clamps both monitors at zero");
                check(BrightnessSync.Step(new DdcMonitor[0], queued, true, 5).Length == 0, "No eligible monitor produces no brightness write");
                queued.Clear(); check(BrightnessSync.Step(new[] { targets[1] }, queued, true, 5).Single().Percent == 65, "Single selected monitor remains adjustable");
                targets[1].Maximum = 200; targets[1].Current = 120;
                check(BrightnessSync.Step(targets, queued, true, 5).All(c => c.Percent == 65), "Synchronization uses percent across different hardware ranges");
                foreach (string modifier in new[] { "CtrlAlt", "RightAlt" })
                    foreach (int key in new[] { 0x78, 0x79 }) check(!BrightnessBinding.Valid(modifier, 0x71, key), "Force hotkey protected from brightness and AltGr conflicts: " + modifier + "/" + key);
                check(!BrightnessBinding.Valid("CtrlAlt", 0x7A, 0x72) && !BrightnessBinding.Valid("CtrlAlt", 0x71, 0x7B), "Existing Ctrl Alt F11/F12 shortcuts cannot be replaced");
                check(!BrightnessBinding.Valid("RightAlt", 0x71, 0x71) && !BrightnessBinding.Valid("RightAlt", 0x69, 0x72), "Duplicate or invalid shortcut rejected");
                check(BrightnessKeys.Normalize(0x15, 0x38, 1) == 0xA5, "Korean extended right Alt scan code recognized");
                check(BrightnessKeys.Normalize(0x15, 0x38, 0) == 0x15, "Unrelated Hangul key not mistaken for right Alt");
                foreach (string modifier in new[] { "RightAlt", "LeftAlt", "CtrlAlt", "CtrlShift" })
                {
                    var binding = new BrightnessBinding(true, modifier, 0x71, 0x72);
                    int[] modifiers = modifier == "RightAlt" ? new[] { 0xA5 } : modifier == "LeftAlt" ? new[] { 0xA4 } : modifier == "CtrlAlt" ? new[] { 0xA2, 0xA4 } : new[] { 0xA2, 0xA0 };
                    foreach (int key in new[] { 0x71, 0x72 })
                    {
                        var filter = new BrightnessKeys(); int direction;
                        foreach (int mod in modifiers) check(!filter.Process(mod, true, binding, out direction), modifier + " modifier alone passes through");
                        check(filter.Process(key, true, binding, out direction) && direction == (key == 0x71 ? -1 : 1), modifier + " dispatches correct brightness direction");
                        check(filter.Process(key, true, binding, out direction) && direction == 0, "Hold does not flood DDC with writes");
                        foreach (int mod in modifiers) filter.Process(mod, false, binding, out direction);
                        check(filter.Process(key, false, null, out direction), "Matched key-up swallowed after modifier release or disable");
                    }
                }
                {
                    var keys = new BrightnessKeys(); int direction; var binding = new BrightnessBinding(true, "RightAlt", 0x71, 0x72);
                    keys.Process(0xA4, true, binding, out direction);
                    check(!keys.Process(0x71, true, binding, out direction), "Left Alt does not trigger right Alt binding");
                    keys = new BrightnessKeys(); keys.Process(0xA2, true, binding, out direction); keys.Process(0xA5, true, binding, out direction);
                    check(keys.Process(0x72, true, binding, out direction) && direction == 1, "AltGr synthesized Ctrl supported");
                    keys = new BrightnessKeys(); keys.Process(0xA5, true, binding, out direction); keys.Process(0x5B, true, binding, out direction);
                    check(!keys.Process(0x72, true, binding, out direction), "Win-modified shortcuts are untouched");
                    keys = new BrightnessKeys(); keys.Process(0xA5, true, null, out direction);
                    check(!keys.Process(0x71, true, new BrightnessBinding(false, "RightAlt", 0x71, 0x72), out direction), "Disabled brightness shortcuts pass through");
                }
                foreach (var invalid in new[] { new DdcRequest { Action = "power-off" }, new DdcRequest { Action = "set", Changes = new[] { new DdcChange { Id = "test", Percent = 101 } } }, new DdcRequest { Action = "set" } })
                {
                    bool rejected = false; try { DdcWorker.Execute(invalid); } catch (ArgumentException) { rejected = true; }
                    check(rejected, "Malformed or non-brightness request rejected before hardware access");
                }
                string temporary = Path.Combine(Path.GetDirectoryName(path), "brightness-test-settings.xml");
                try
                {
                    File.WriteAllText(temporary, "<Preferences><DeviceName>legacy</DeviceName><WaitValue>60</WaitValue><Minutes>false</Minutes><CenterPercent>80</CenterPercent></Preferences>");
                    var p = Preferences.Read(temporary);
                    check(p.Brightness.Modifier == "RightAlt" && p.Brightness.Step == 5 && p.CenterPercent == 80 && p.WaitValue == 60, "Legacy settings retain layout and protection with brightness defaults");
                    check(p.Brightness.SyncEnabled, "Legacy settings enable synchronized brightness by default");
                    p.Brightness.Monitors = new[] { new MonitorBrightnessPreference { Id = "physical-A", Selected = false }, new MonitorBrightnessPreference { Id = "physical-B", Selected = true } }; p.Brightness.Modifier = "CtrlShift"; p.Brightness.UpKey = 0x75; p.Brightness.SyncEnabled = false; p.Write(temporary);
                    var loaded = Preferences.Read(temporary);
                    check(loaded.Brightness.Monitors.Length == 2 && !loaded.Brightness.Monitors[0].Selected && !loaded.Brightness.SyncEnabled && loaded.Brightness.UpKey == 0x75, "Per-monitor targets, sync setting and custom keys persist");
                    string legacy = File.ReadAllText(temporary).Replace("<Selected>false</Selected>", "<Selected>false</Selected><MaxNits>300</MaxNits>");
                    File.WriteAllText(temporary, legacy); loaded = Preferences.Read(temporary);
                    check(loaded.Brightness.Monitors.Length == 2 && loaded.Brightness.UpKey == 0x75, "Removed maximum-nit XML fields do not break legacy preferences");
                    loaded.Write(temporary); check(!File.ReadAllText(temporary).Contains("MaxNits"), "Removed maximum-nit field is no longer saved");
                    p.Brightness.Step = 100; p.Write(temporary); loaded = Preferences.Read(temporary);
                    check(loaded.Brightness.Step == 5, "Invalid saved settings normalized");
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                File.WriteAllLines(path, lines.ToArray()); return 0;
            }
            catch (Exception e) { lines.Add(e.ToString()); File.WriteAllLines(path, lines.ToArray()); return 1; }
        }
    }
}
