using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using W = System.Windows;
using C = System.Windows.Controls;

namespace MonitorShade
{
    public sealed class ManualShadePreferences
    {
        public bool Enabled = true;
        public string FirstDevice, SecondDevice;
    }
    internal interface IManualCover : IDisposable { void Cover(); }
    internal sealed class ManualCover : IManualCover
    {
        private readonly ShadeWindow window;
        internal ManualCover(Rectangle bounds) { window = new ShadeWindow(bounds); }
        public void Cover() { window.Cover(); }
        public void Dispose() { window.Dispose(); }
    }
    internal sealed class PointerEntry
    {
        private readonly Rectangle bounds;
        private bool outside;
        internal PointerEntry(Rectangle area, Point initial) { bounds = area; outside = !area.Contains(initial); }
        internal bool Entered(Point point)
        {
            if (!bounds.Contains(point)) { outside = true; return false; }
            return outside;
        }
    }
    internal sealed class ManualShadeController : IDisposable
    {
        private sealed class CoverState { internal IManualCover Cover; internal PointerEntry Entry; }
        private readonly Dictionary<string, CoverState> covers = new Dictionary<string, CoverState>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<bool> available;
        private readonly Func<Point> pointer;
        private readonly Func<Rectangle, IManualCover> factory;
        private readonly Action<string, bool> hold;
        private readonly Timer timer = new Timer { Interval = 50 };
        internal event Action Changed;
        internal int Count { get { return covers.Count; } }
        internal ManualShadeController(Action<string, bool> pauseTarget)
            : this(DesktopAccess.Available, ReadPointer, delegate(Rectangle area) { return new ManualCover(area); }, pauseTarget) { }
        internal ManualShadeController(Func<bool> desktop, Func<Point> readPointer, Func<Rectangle, IManualCover> create, Action<string, bool> pauseTarget)
        { available = desktop; pointer = readPointer; factory = create; hold = pauseTarget; timer.Tick += delegate { Poll(); }; }
        private static Point ReadPointer()
        {
            Native.Point point;
            if (!Native.GetCursorPos(out point)) throw new PointerUnavailableException(Marshal.GetLastWin32Error());
            return new Point(point.X, point.Y);
        }
        internal void Toggle(string device, Rectangle bounds)
        {
            if (covers.ContainsKey(device)) { Release(device); return; }
            if (string.IsNullOrEmpty(device) || bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentException("가릴 모니터를 선택하세요.");
            if (!available()) return;
            Point initial;
            try { initial = pointer(); } catch (PointerUnavailableException) { return; }
            IManualCover cover = null;
            try
            {
                hold(device, true);
                cover = factory(bounds); cover.Cover();
                covers.Add(device, new CoverState { Cover = cover, Entry = new PointerEntry(bounds, initial) });
            }
            catch { if (cover != null) cover.Dispose(); hold(device, false); throw; }
            timer.Start(); if (Changed != null) Changed();
        }
        internal void Poll()
        {
            if (covers.Count == 0) return;
            try
            {
                if (!available()) { Clear(); return; }
                Point position = pointer();
                foreach (var pair in covers.ToArray()) if (pair.Value.Entry.Entered(position)) Release(pair.Key);
            }
            catch (PointerUnavailableException) { Clear(); }
        }
        private void Release(string device)
        {
            CoverState state;
            if (!covers.TryGetValue(device, out state)) return;
            covers.Remove(device);
            try { state.Cover.Dispose(); }
            finally { hold(device, false); if (covers.Count == 0) timer.Stop(); if (Changed != null) Changed(); }
        }
        internal void Clear() { foreach (string device in covers.Keys.ToArray()) Release(device); }
        public void Dispose() { Clear(); timer.Dispose(); }
    }
    internal sealed class ManualShadePage : IDisposable
    {
        private sealed class Choice { internal string Device; public string Label { get; set; } public override string ToString() { return Label; } }
        private readonly C.UserControl ui;
        private readonly bool preview;
        private readonly string path;
        private readonly Func<IntPtr> owner;
        private ManualShadePreferences settings;
        private IntPtr registeredWindow;
        private readonly bool[] registered = new bool[2];
        internal readonly ManualShadeController Controller;
        internal ManualShadePage(C.UserControl root, ManualShadePreferences preferences, string preferencesPath, bool previewMode, Func<IntPtr> window, WatchGroup group)
        {
            ui = root; preview = previewMode; path = preferencesPath; owner = window;
            settings = new ManualShadePreferences { Enabled = preferences.Enabled, FirstDevice = preferences.FirstDevice ?? "", SecondDevice = preferences.SecondDevice ?? "" };
            Controller = new ManualShadeController(group.Hold);
            var screens = Screen.AllScreens.OrderByDescending(s => s.Primary).ThenBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToArray();
            if (preferences.FirstDevice == null) settings.FirstDevice = screens.Select(s => s.DeviceName).FirstOrDefault() ?? "";
            if (preferences.SecondDevice == null) settings.SecondDevice = screens.Skip(1).Select(s => s.DeviceName).FirstOrDefault() ?? "";
            Find<C.CheckBox>("ManualEnabled").IsChecked = settings.Enabled;
            RefreshTargets();
            Find<C.Button>("ApplyManual").Click += delegate { Apply(); };
        }
        private T Find<T>(string name) where T : class { return (T)ui.FindName(name); }
        internal void RefreshTargets()
        {
            var screens = Screen.AllScreens.OrderByDescending(s => s.Primary).ThenBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToArray();
            foreach (string name in new[] { "ManualFirst", "ManualSecond" })
            {
                var combo = Find<C.ComboBox>(name);
                var before = combo.SelectedItem as Choice;
                string selected = before == null ? (name == "ManualFirst" ? settings.FirstDevice : settings.SecondDevice) : before.Device;
                var values = new List<Choice> { new Choice { Device = "", Label = "사용 안 함" } };
                values.AddRange(screens.Select((s, i) => new Choice { Device = s.DeviceName, Label = "모니터 " + (i + 1) + (s.Primary ? " (기본)" : "") }));
                if (!values.Any(v => v.Device == selected)) values.Add(new Choice { Device = selected, Label = "연결되지 않은 모니터" });
                combo.DisplayMemberPath = "Label"; combo.ItemsSource = values; combo.SelectedItem = values.First(v => v.Device == selected);
            }
        }
        internal bool Apply()
        {
            var first = Find<C.ComboBox>("ManualFirst").SelectedItem as Choice;
            var second = Find<C.ComboBox>("ManualSecond").SelectedItem as Choice;
            var updated = new ManualShadePreferences { Enabled = Find<C.CheckBox>("ManualEnabled").IsChecked == true, FirstDevice = first == null ? "" : first.Device, SecondDevice = second == null ? "" : second.Device };
            try
            {
                if (!preview) { var saved = Preferences.Read(path); saved.ManualShade = updated; saved.Write(path); }
                settings = updated; Controller.Clear(); Register(); return true;
            }
            catch (Exception e) { Message("강제 가림 설정 저장 실패: " + e.Message); return false; }
        }
        private void Message(string text)
        { var label = Find<C.TextBlock>("ManualStatus"); label.Text = text; label.Visibility = text.Length == 0 ? W.Visibility.Collapsed : W.Visibility.Visible; }
        internal void Register()
        {
            Unregister(); Message("");
            if (preview || !settings.Enabled) return;
            registeredWindow = owner();
            string[] devices = { settings.FirstDevice, settings.SecondDevice };
            var failures = new List<string>();
            for (int i = 0; i < 2; i++)
            {
                if (string.IsNullOrEmpty(devices[i])) continue;
                registered[i] = Native.RegisterHotKey(registeredWindow, 3 + i, 0x4000 | 3, (uint)(0x78 + i));
                if (!registered[i]) failures.Add("Ctrl+Alt+F" + (9 + i));
            }
            if (failures.Count > 0) Message(string.Join(", ", failures) + " 등록 실패 · 다른 앱의 단축키 사용 여부를 확인하세요.");
        }
        internal void Shortcut(int slot)
        {
            if (preview || !settings.Enabled || slot < 0 || slot > 1 || !registered[slot]) return;
            string device = slot == 0 ? settings.FirstDevice : settings.SecondDevice;
            Screen screen = StartupRegistration.FindTarget(device);
            if (screen == null) { Message("가릴 모니터가 연결되어 있지 않습니다."); return; }
            try { Controller.Toggle(device, screen.Bounds); Message(""); }
            catch (Exception e) { Message("검은 화면을 표시하지 못했습니다: " + e.Message); }
        }
        private void Unregister()
        {
            for (int i = 0; i < 2; i++) { if (registered[i]) Native.UnregisterHotKey(registeredWindow, 3 + i); registered[i] = false; }
            registeredWindow = IntPtr.Zero;
        }
        internal void VerifyUi(Action<bool, string> check)
        {
            check(Find<C.ComboBox>("ManualFirst").Items.Count >= 2 && Find<C.ComboBox>("ManualSecond").Items.Count >= 2, "Force shade shortcuts expose monitor targets");
            check(Find<C.ComboBox>("ManualFirst").SelectedItem.ToString().StartsWith("모니터 "), "Force target selector displays a readable monitor label");
            check(Find<C.CheckBox>("ManualEnabled").IsChecked == true && Apply(), "Force shade can be enabled without activating overlays or hardware in preview");
            Find<C.CheckBox>("ManualEnabled").IsChecked = false; check(Apply() && !settings.Enabled, "Force shade shortcuts can be disabled");
            Find<C.CheckBox>("ManualEnabled").IsChecked = true; Apply();
        }
        public void Dispose() { Unregister(); Controller.Dispose(); }
    }
}
