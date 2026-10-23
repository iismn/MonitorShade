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
    internal static class LayoutVerification
    {
        internal static int Run(string path, bool live)
        {
            List<string> lines = new List<string>();
            Action<bool, string> check = delegate(bool valid, string name) { if (!valid) throw new Exception("FAIL: " + name); lines.Add("PASS: " + name); };
            try
            {
                Rectangle area = new Rectangle(-2160, -745, 2160, 3780);
                foreach (LayoutDirection direction in new[] { LayoutDirection.Left, LayoutDirection.Right, LayoutDirection.Up, LayoutDirection.Down })
                {
                    Rectangle half = LayoutGeometry.Bounds(area, direction, 0, 65), twoThirds = LayoutGeometry.Bounds(area, direction, 1, 65);
                    check(area.Contains(half) && area.Contains(twoThirds), direction + " stays inside negative-coordinate portrait work area");
                    bool horizontal = direction == LayoutDirection.Left || direction == LayoutDirection.Right;
                    check((horizontal ? half.Width : half.Height) == (horizontal ? area.Width : area.Height) / 2, direction + " first press is exactly half");
                    check((horizontal ? twoThirds.Width : twoThirds.Height) == (horizontal ? area.Width : area.Height) * 2 / 3, direction + " second press is two thirds");
                    check(LayoutGeometry.Bounds(area, direction, 2, 65) == area, direction + " third press fills work area");
                    check(direction == LayoutDirection.Left ? half.Left == area.Left : direction == LayoutDirection.Right ? half.Right == area.Right : direction == LayoutDirection.Up ? half.Top == area.Top : half.Bottom == area.Bottom, direction + " anchors to requested edge");
                }
                foreach (Rectangle work in new[] { area, new Rectangle(0, 0, 3840, 2100), new Rectangle(50, 40, 3440, 1400) })
                    foreach (int percent in new[] { 10, 65, 100 })
                    {
                        Rectangle center = LayoutGeometry.Bounds(work, LayoutDirection.Center, 0, percent);
                        check(work.Contains(center) && Math.Abs(center.Width * 9 - center.Height * 16) <= 8 && Math.Abs(center.Left * 2 + center.Width - work.Left * 2 - work.Width) <= 1 && Math.Abs(center.Top * 2 + center.Height - work.Top * 2 - work.Height) <= 1, "Centered 16:9 fits " + work.Size + " at " + percent + "%");
                    }
                Rectangle wide = new Rectangle(0, 0, 3840, 2160);
                check(LayoutGeometry.Bounds(wide, LayoutDirection.Center, 0, 65).Width == 2496, "65 percent means work area width");
                check(LayoutGeometry.Bounds(wide, LayoutDirection.Center, 1, 65) == wide, "Center second press fills work area");
                foreach (Rectangle work in new[] { area, wide, new Rectangle(-3441, 25, 3441, 1399), new Rectangle(0, 0, 1, 1) })
                    for (int step = 0; step < 3; step++)
                    {
                        Rectangle scaled = LayoutGeometry.Bounds(work, LayoutDirection.Scale, step, 65);
                        double fraction = step == 0 ? .5 : step == 1 ? .7 : 1;
                        check(scaled.Width == Math.Max(1, (int)Math.Round(work.Width * fraction)) && scaled.Height == Math.Max(1, (int)Math.Round(work.Height * fraction)), "Scale uses both work-area dimensions at " + fraction + " on " + work.Size);
                        check(work.Contains(scaled) && Math.Abs(scaled.Left * 2 + scaled.Width - work.Left * 2 - work.Width) <= 1 && Math.Abs(scaled.Top * 2 + scaled.Height - work.Top * 2 - work.Height) <= 1, "Scale stays centered within portrait/landscape/negative bounds");
                    }
                foreach (int win in new[] { 0x5B, 0x5C })
                    foreach (int key in new[] { 0x25, 0x26, 0x27, 0x28, 0x2E, 0x08 })
                    {
                        LayoutKeys keys = new LayoutKeys(); LayoutDirection? command;
                        check(!keys.Process(win, true, true, out command), "Win key alone passes through: " + win);
                        check(keys.Process(key, true, true, out command) && command == (key == 0x2E ? LayoutDirection.Center : key == 0x08 ? LayoutDirection.Scale : (LayoutDirection)(key - 0x25)), "Exact shortcut dispatched: " + win + "/" + key);
                        check(keys.Process(key, true, true, out command) && !command.HasValue, "Held shortcut does not repeatedly cycle");
                        keys.Process(win, false, true, out command);
                        check(keys.Process(key, false, false, out command) && !command.HasValue, "Consumed key-up stays consumed after Win release or feature disable");
                    }
                foreach (int modifier in new[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 })
                {
                    LayoutKeys keys = new LayoutKeys(); LayoutDirection? command;
                    keys.Process(0x5B, true, true, out command); keys.Process(modifier, true, true, out command);
                    check(!keys.Process(0x25, true, true, out command) && !command.HasValue, "Extra modifier preserves Windows shortcut: " + modifier);
                    check(!keys.Process(0x08, true, true, out command) && !command.HasValue, "Modified Win + Backspace passes through: " + modifier);
                }
                {
                    LayoutKeys keys = new LayoutKeys(); LayoutDirection? command;
                    check(!keys.Process(0x25, true, true, out command), "Arrow alone passes through"); keys.Process(0x25, false, true, out command);
                    keys.Process(0x5B, true, true, out command);
                    check(!keys.Process(0x25, true, false, out command), "Disabled feature or ineligible window passes shortcut through");
                    check(!keys.Process(0x45, true, true, out command), "Unrelated Win shortcut passes through");
                    check(!keys.Process(0x08, true, false, out command), "Disabled Win + Backspace passes through");
                    keys.Process(0x08, false, false, out command); keys.Process(0x5B, false, true, out command);
                    check(!keys.Process(0x08, true, true, out command) && !command.HasValue, "Backspace alone remains normal text editing");
                }
                foreach (int modifiers in new[] { 1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 })
                    foreach (string action in LayoutBindings.Actions)
                    {
                        var bindings = LayoutBindings.Defaults(); var binding = bindings.First(b => b.Action == action); binding.Modifiers = modifiers; binding.Key = 0x70;
                        check(LayoutBindings.Error(bindings, null) == null, "Custom chord validates: " + modifiers + "/" + action);
                        var keys = new LayoutKeys(); LayoutDirection? command;
                        foreach (var pair in new[] { new[] { 1, 0xA4 }, new[] { 2, 0xA2 }, new[] { 4, 0xA0 }, new[] { 8, 0x5B } })
                            if ((modifiers & pair[0]) != 0) keys.Process(pair[1], true, true, bindings, out command);
                        check(keys.Process(0x70, true, true, bindings, out command) && command.ToString() == action, "Custom chord dispatches exactly one action: " + modifiers + "/" + action);
                        check(keys.Process(0x70, true, true, bindings, out command) && command == null, "Custom chord repeat does not cycle");
                        check(keys.Process(0x70, false, false, LayoutBindings.Defaults(), out command), "Held shortcut release is consumed after configuration replacement");
                    }
                {
                    var bindings = LayoutBindings.Defaults(); bindings[0].Modifiers = 6; bindings[0].Key = 0x41;
                    var keys = new LayoutKeys(); LayoutDirection? command;
                    keys.Seed(0x5B);
                    check(!keys.Process(0x25, true, true, bindings, out command), "Reassigned action no longer consumes its old Win shortcut");
                    keys = new LayoutKeys(); keys.Seed(0xA2); keys.Seed(0xA0);
                    check(!keys.Process(0x41, true, false, bindings, out command), "Disabled custom shortcut passes through");
                    keys.Process(0x41, false, true, bindings, out command); keys.Seed(0xA4);
                    check(!keys.Process(0x41, true, true, bindings, out command), "Extra modifier does not match a custom chord");
                    keys = new LayoutKeys(); keys.Seed(0x11); keys.Seed(0x10);
                    check(keys.Process(0x41, true, true, bindings, out command) && command == LayoutDirection.Left, "Generic Ctrl and Shift states match the custom chord");
                    bindings[1].Modifiers = 6; bindings[1].Key = 0x41;
                    check(LayoutBindings.Error(bindings, null) != null, "Duplicate shortcuts rejected");
                    check(LayoutBindings.Normalize(bindings)[0].Key == 0x25, "Invalid stored bindings restore defaults");
                    foreach (var invalid in new[] { new[] {0,65}, new[] {4,65}, new[] {16,65}, new[] {8,0}, new[] {3,46}, new[] {8,76}, new[] {1,115}, new[] {3,120}, new[] {3,123} }) {
                        bindings = LayoutBindings.Defaults(); bindings[0].Modifiers = invalid[0]; bindings[0].Key = invalid[1];
                        check(LayoutBindings.Error(bindings, null) != null, "Invalid or reserved chord rejected: " + invalid[0] + "/" + invalid[1]);
                    }
                    foreach (string modifier in new[] { "RightAlt", "LeftAlt", "CtrlAlt", "CtrlShift" }) {
                        bindings = LayoutBindings.Defaults(); bindings[0].Modifiers = modifier == "CtrlShift" ? 6 : modifier == "CtrlAlt" ? 3 : 1; bindings[0].Key = 0x71;
                        check(LayoutBindings.Error(bindings, new BrightnessBinding(true, modifier, 0x71, 0x72)) != null, "Brightness conflict rejected: " + modifier);
                        check(LayoutBindings.Error(bindings, new BrightnessBinding(false, modifier, 0x71, 0x72)) == null, "Disabled brightness binding does not reserve keys");
                    }
                    bindings = LayoutBindings.Defaults(); bindings[0].Modifiers = 3; bindings[0].Key = 0x71;
                    check(LayoutBindings.Error(bindings, new BrightnessBinding(true, "RightAlt", 0x71, 0x72)) != null, "AltGr synthetic Ctrl+Alt brightness conflict rejected");
                }
                string temporary = Path.Combine(Path.GetDirectoryName(path), "layout-preferences-test.xml");
                try
                {
                    File.WriteAllText(temporary, "<Preferences><DeviceName>legacy</DeviceName><WaitValue>60</WaitValue><Minutes>false</Minutes></Preferences>");
                    Preferences p = Preferences.Read(temporary);
                    check(p.LayoutEnabled && p.CenterPercent == 65 && p.SelectedDevices()[0] == "legacy" && p.WaitValue == 60, "Legacy settings keep monitor and delay with layout defaults");
                    check(p.LayoutShortcuts.Length == 6 && p.LayoutShortcuts[5].Key == 0x08, "Legacy settings receive all default layout shortcuts");
                    p.LayoutShortcuts[5].Modifiers = 6; p.LayoutShortcuts[5].Key = 0x52;
                    p.LayoutEnabled = false; p.CenterPercent = 80; p.Write(temporary); p = Preferences.Read(temporary);
                    check(!p.LayoutEnabled && p.CenterPercent == 80 && p.WaitValue == 60, "Layout options round trip without changing protection settings");
                    check(p.LayoutShortcuts[5].Modifiers == 6 && p.LayoutShortcuts[5].Key == 0x52, "Custom resize shortcut survives XML save and reload");
                    p.CenterPercent = 101; p.Write(temporary); check(Preferences.Read(temporary).CenterPercent == 65, "Invalid stored percentage restores valid default");
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                if (live) Live(check);
                lines.Add(live ? "Live placement uses only owned test windows; no Windows-key input is injected." : "Geometry, key-filter and persistence checks completed.");
                File.WriteAllLines(path, lines.ToArray()); return 0;
            }
            catch (Exception e) { lines.Add(e.ToString()); File.WriteAllLines(path, lines.ToArray()); return 1; }
        }
        private static void Pump(WindowArranger arranger)
        {
            Stopwatch wait = Stopwatch.StartNew();
            while (arranger.Busy && wait.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(10); }
            if (arranger.Busy) throw new Exception("Placement timed out");
            Application.DoEvents();
        }
        private static void Live(Action<bool, string> check)
        {
            using (LayoutKeyboardHook hook = new LayoutKeyboardHook(delegate { })) { check(hook.Installed && !hook.Enabled, "Dedicated keyboard hook installs in disabled test mode"); }
            using (WindowArranger arranger = new WindowArranger())
            using (Form sample = new Form { Text = "MonitorShade 창 배치 진단", StartPosition = FormStartPosition.Manual, AutoScaleMode = AutoScaleMode.None, MinimumSize = new Size(80, 60), Size = new Size(640, 400) })
            {
                string error = null; arranger.Failed += delegate(string message) { error = message; };
                sample.Show();
                // STARTUPINFO may suppress the first window when the CLI runner is hidden.
                LayoutNative.ShowWindowAsync(sample.Handle, 5); Application.DoEvents();
                check(LayoutNative.CanArrange(sample.Handle), "Resizable test window accepted");
                foreach (Screen screen in Screen.AllScreens)
                {
                    sample.WindowState = FormWindowState.Normal; sample.Bounds = new Rectangle(screen.WorkingArea.X + 40, screen.WorkingArea.Y + 40, 640, 400); Application.DoEvents();
                    foreach (LayoutDirection direction in new[] { LayoutDirection.Left, LayoutDirection.Up, LayoutDirection.Right, LayoutDirection.Down, LayoutDirection.Center, LayoutDirection.Scale })
                    {
                        arranger.Reset();
                        int maximum = direction == LayoutDirection.Center ? 2 : 3;
                        for (int i = 0; i <= maximum; i++)
                        {
                            error = null; arranger.Apply(sample.Handle, direction, 65); Pump(arranger);
                            int step = i % maximum;
                            check(error == null && (step == maximum - 1 ? LayoutNative.IsZoomed(sample.Handle) : !LayoutNative.IsZoomed(sample.Handle) && LayoutGeometry.Near(LayoutNative.VisibleBounds(sample.Handle), LayoutGeometry.Bounds(screen.WorkingArea, direction, step, 65))), screen.DeviceName + " " + direction + " live step " + step + (error == null ? "" : ": " + error));
                        }
                    }
                }
                arranger.Reset(); error = null;
                arranger.Apply(sample.Handle, LayoutDirection.Left, 65); Pump(arranger);
                arranger.Apply(sample.Handle, LayoutDirection.Right, 65); Pump(arranger);
                Rectangle area = Screen.FromHandle(sample.Handle).WorkingArea;
                check(error == null && LayoutGeometry.Near(LayoutNative.VisibleBounds(sample.Handle), LayoutGeometry.Bounds(area, LayoutDirection.Right, 0, 65)), "Changing direction resets to half");
                sample.Left += 45; Application.DoEvents(); arranger.Apply(sample.Handle, LayoutDirection.Right, 65); Pump(arranger);
                check(LayoutGeometry.Near(LayoutNative.VisibleBounds(sample.Handle), LayoutGeometry.Bounds(area, LayoutDirection.Right, 0, 65)), "Manual move resets cycle");
                arranger.Reset(); arranger.Apply(sample.Handle, LayoutDirection.Up, 65); arranger.Apply(sample.Handle, LayoutDirection.Up, 65); arranger.Apply(sample.Handle, LayoutDirection.Up, 65); Pump(arranger);
                check(LayoutNative.IsZoomed(sample.Handle), "Rapid repeated presses serialize to maximum");
                arranger.Apply(sample.Handle, LayoutDirection.Center, 80); Pump(arranger);
                check(LayoutGeometry.Near(LayoutNative.VisibleBounds(sample.Handle), LayoutGeometry.Bounds(area, LayoutDirection.Center, 0, 80)), "Edited center percentage changes real window");
                using (Form fixedWindow = new Form { FormBorderStyle = FormBorderStyle.FixedDialog, Text = "Fixed test" }) { fixedWindow.Show(); Application.DoEvents(); check(!LayoutNative.CanArrange(fixedWindow.Handle), "Fixed dialogs excluded"); }
                check(!LayoutNative.CanArrange(IntPtr.Zero), "No foreground window ignored");
            }
        }
    }
}
