using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;

namespace MonitorShade
{
    internal sealed class BrightnessPage : IDisposable
    {
        private sealed class Card
        {
            internal DdcMonitor Monitor;
            internal C.Slider Slider;
            internal AnimatedPercent Percent;
            internal C.TextBlock Status;
            internal C.CheckBox Selected;
            internal bool Updating;
        }
        private readonly C.UserControl ui;
        private readonly bool preview;
        private readonly string preferencesPath;
        private readonly DdcClient client = new DdcClient();
        private readonly Timer debounce = new Timer { Interval = 300 };
        private readonly Dictionary<string, Card> cards = new Dictionary<string, Card>();
        private readonly Dictionary<string, int> desired = new Dictionary<string, int>(), pending = new Dictionary<string, int>();
        private BrightnessPreferences settings;
        private DdcMonitor[] monitors = new DdcMonitor[0];
        private bool busy, disposed, suspended, updatingLink, updatingMaster;
        private DateTime lastRead = DateTime.MinValue;
        private int waitingSteps;
        private BrightnessToast toast;
        internal event Action BindingChanged;
        internal Func<BrightnessBinding, string> ValidateBinding;
        internal BrightnessBinding ConfiguredBinding { get { return new BrightnessBinding(settings.HotkeysEnabled, settings.Modifier, settings.DownKey, settings.UpKey); } }
        internal BrightnessBinding Binding { get { return new BrightnessBinding(settings.HotkeysEnabled && !suspended && !preview, settings.Modifier, settings.DownKey, settings.UpKey); } }
        internal BrightnessPage(C.UserControl root, BrightnessPreferences preferences, string path, bool previewMode)
        {
            ui = root; settings = preferences; settings.Normalize(); preferencesPath = path; preview = previewMode;
            var modifier = Find<C.ComboBox>("BrightnessModifier");
            modifier.ItemsSource = new[] { "오른쪽 Alt", "왼쪽 Alt", "Ctrl + Alt", "Ctrl + Shift" };
            modifier.SelectedIndex = Array.IndexOf(new[] { "RightAlt", "LeftAlt", "CtrlAlt", "CtrlShift" }, settings.Modifier);
            foreach (string name in new[] { "BrightnessDown", "BrightnessUp" }) Find<C.ComboBox>(name).ItemsSource = Enumerable.Range(1, 12).Select(n => "F" + n).ToArray();
            Find<C.ComboBox>("BrightnessDown").SelectedIndex = settings.DownKey - 0x70;
            Find<C.ComboBox>("BrightnessUp").SelectedIndex = settings.UpKey - 0x70;
            Find<C.ComboBox>("BrightnessTarget").ItemsSource = new[] { "선택한 모니터 함께", "마우스가 있는 모니터" };
            Find<C.ComboBox>("BrightnessTarget").SelectedIndex = settings.Target == "Pointer" ? 1 : 0;
            Find<C.CheckBox>("BrightnessHotkeys").IsChecked = settings.HotkeysEnabled;
            var sync = Find<C.CheckBox>("BrightnessSync"); sync.IsChecked = settings.SyncEnabled;
            sync.Checked += delegate { SaveLinking(); }; sync.Unchecked += delegate { SaveLinking(); };
            Find<C.TextBox>("BrightnessStep").Text = settings.Step.ToString(CultureInfo.InvariantCulture);
            Find<C.Button>("ApplyBrightness").Click += delegate { ApplySettings(); };
            Find<C.Button>("RefreshBrightness").Click += async delegate { await Refresh(false); };
            Find<C.Button>("BrightnessCapabilities").Click += async delegate { await Refresh(true); };
            Find<C.Slider>("MasterBrightness").ValueChanged += delegate { if (!updatingMaster) QueueMaster((int)Find<C.Slider>("MasterBrightness").Value); };
            Find<C.Button>("MasterMinus").Click += delegate { StepMaster(-settings.Step); };
            Find<C.Button>("MasterPlus").Click += delegate { StepMaster(settings.Step); };
            Find<C.TextBox>("MasterNumber").LostKeyboardFocus += delegate { CommitMaster(); };
            Find<C.TextBox>("MasterNumber").KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Enter) { CommitMaster(); e.Handled = true; } };
            AddWheel(Find<C.Slider>("MasterBrightness"), StepMaster);
            debounce.Tick += async delegate { debounce.Stop(); if (busy) { debounce.Start(); return; } await Flush(); };
            if (preview) SetSnapshot(new[] {
                new DdcMonitor { Id = "preview-1", Device = "DISPLAY1", Name = "PG27UCWM", Primary = true, Width = 3840, Height = 2160, Supported = true, Current = 70, Maximum = 100, PeakNits = 465, FullFrameNits = 265, Status = "DDC/CI 밝기 읽기 성공" },
                new DdcMonitor { Id = "preview-2", Device = "DISPLAY5", Name = "PG27UCWM", X = -2160, Width = 2160, Height = 3840, Supported = true, Current = 74, Maximum = 100, PeakNits = 465, FullFrameNits = 265, Status = "DDC/CI 밝기 읽기 성공" }
            });
        }
        private T Find<T>(string name) where T : class { return (T)ui.FindName(name); }
        internal object WebSnapshot()
        {
            return monitors.OrderBy(m => m.X).ThenBy(m => m.Y).Select(m => new {
                id = m.Id, name = m.Name, device = m.Device, primary = m.Primary,
                width = m.Width, height = m.Height, x = m.X, y = m.Y,
                supported = m.Supported, selected = IsSelected(m.Id),
                percent = desired.ContainsKey(m.Id) ? desired[m.Id] : m.Percent,
                status = cards.ContainsKey(m.Id) ? cards[m.Id].Status.Text : m.Status,
                capabilities = m.Capabilities, peakNits = m.PeakNits, fullFrameNits = m.FullFrameNits
            }).ToArray();
        }
        internal void WebBrightness(string id, int percent, bool step)
        {
            if (step && (percent < -100 || percent > 100) || !step && (percent < 0 || percent > 100)) throw new ArgumentOutOfRangeException("percent");
            if (id == "master") { if (step) StepMaster(percent); else QueueMaster(percent); }
            else if (cards.ContainsKey(id)) { if (step) QueueStep(id, percent); else QueueAbsolute(id, percent); }
            else throw new ArgumentException("연결된 모니터가 아닙니다.");
        }
        internal void WebSelected(string id, bool selected)
        { if (!cards.ContainsKey(id)) throw new ArgumentException("연결된 모니터가 아닙니다."); cards[id].Selected.IsChecked = selected; }
        private void Message(string text, bool help = false)
        {
            var label = Find<C.TextBlock>(help ? "DdcStatus" : "BrightnessStatus");
            label.Text = text; label.Visibility = string.IsNullOrEmpty(text) ? W.Visibility.Collapsed : W.Visibility.Visible;
        }
        private void ShortcutMessage(string text)
        {
            var label = Find<C.TextBlock>("ShortcutsStatus");
            label.Text = text; label.Visibility = string.IsNullOrEmpty(text) ? W.Visibility.Collapsed : W.Visibility.Visible;
        }
        private C.TextBlock Text(string text, bool caption)
        { var item = new C.TextBlock { Text = text, TextWrapping = W.TextWrapping.Wrap }; if (caption) item.Style = (W.Style)ui.Resources["Caption"]; return item; }
        internal void Suspend(bool value) { suspended = value; pending.Clear(); desired.Clear(); waitingSteps = 0; debounce.Stop(); if (BindingChanged != null) BindingChanged(); }
        internal void Invalidate() { lastRead = DateTime.MinValue; pending.Clear(); desired.Clear(); }
        internal async Task Refresh(bool capabilities)
        {
            if (disposed || preview || suspended || busy) return;
            busy = true; Message(capabilities ? "지원 정보 읽는 중…" : "불러오는 중…", capabilities);
            if (capabilities) Find<C.StackPanel>("DdcDetails").Children.Clear();
            Find<C.Button>("RefreshBrightness").IsEnabled = false; Find<C.Button>("BrightnessCapabilities").IsEnabled = false;
            try
            {
                DdcResponse result = await client.Send(new DdcRequest { Capabilities = capabilities });
                if (disposed) return;
                if (!string.IsNullOrEmpty(result.Error)) Message(result.Error, capabilities);
                else { SetSnapshot(result.Monitors); lastRead = DateTime.UtcNow; Message(capabilities ? (result.Monitors.Any(m => !string.IsNullOrEmpty(m.Capabilities)) ? "지원 정보 읽기 완료" : "지원 정보 응답 없음") : "", capabilities); }
            }
            finally
            {
                busy = false;
                if (!disposed) { Find<C.Button>("RefreshBrightness").IsEnabled = true; Find<C.Button>("BrightnessCapabilities").IsEnabled = true; if (pending.Count > 0) debounce.Start(); }
            }
            if (waitingSteps != 0 && !disposed) { int delta = waitingSteps; waitingSteps = 0; QueueDelta(delta); }
        }
        internal async void Shortcut(int direction)
        {
            if (disposed || suspended || !settings.HotkeysEnabled || preview) return;
            if (monitors.Length == 0 || (!busy && pending.Count == 0 && (DateTime.UtcNow - lastRead).TotalSeconds > 5))
            {
                waitingSteps = Math.Max(-100, Math.Min(100, waitingSteps + direction * settings.Step));
                if (!busy) await Refresh(false);
                return;
            }
            QueueDelta(direction * settings.Step);
        }
        private void QueueDelta(int delta)
        {
            System.Drawing.Point pointer = Point.Empty;
            if (settings.Target == "Pointer")
            {
                Native.Point cursor;
                if (!Native.GetCursorPos(out cursor)) { Message("마우스 위치를 확인할 수 없습니다. 잠금 해제 후 다시 시도하세요."); return; }
                pointer = new Point(cursor.X, cursor.Y);
            }
            var targets = monitors.Where(m => m.Supported && (settings.Target == "Pointer" ? m.Bounds.Contains(pointer) : IsSelected(m.Id))).ToArray();
            if (settings.Target == "Pointer" && targets.Length > 0 && settings.SyncEnabled && IsSelected(targets[0].Id)) targets = LinkedTargets(targets[0].Id);
            if (targets.Length == 0) { Message("조절할 모니터가 없습니다. 연결 상태와 단축키 대상을 확인하세요."); ShowToast("밝기 조절 불가", "대상 모니터를 확인하세요."); return; }
            foreach (var change in BrightnessSync.Step(targets, desired, settings.SyncEnabled, delta)) Queue(change.Id, change.Percent);
        }
        private bool IsSelected(string id) { var saved = settings.Monitors.FirstOrDefault(m => m.Id == id); return saved != null && saved.Selected; }
        private DdcMonitor[] LinkedTargets(string id)
        { return monitors.Where(m => m.Supported && (m.Id == id || (settings.SyncEnabled && IsSelected(id) && IsSelected(m.Id)))).ToArray(); }
        private void QueueAbsolute(string id, int percent)
        { foreach (var target in LinkedTargets(id)) Queue(target.Id, percent); }
        private void QueueStep(string id, int delta)
        { foreach (var change in BrightnessSync.Step(LinkedTargets(id), desired, settings.SyncEnabled, delta)) Queue(change.Id, change.Percent); }
        private DdcMonitor[] MasterTargets() { return monitors.Where(m => m.Supported && IsSelected(m.Id)).ToArray(); }
        private void QueueMaster(int value) { foreach (var monitor in MasterTargets()) Queue(monitor.Id, value); }
        private void StepMaster(int delta) { foreach (var change in BrightnessSync.Step(MasterTargets(), desired, true, delta)) Queue(change.Id, change.Percent); }
        private void CommitMaster()
        {
            int value;
            if (int.TryParse(Find<C.TextBox>("MasterNumber").Text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0 && value <= 100) { QueueMaster(value); Message(""); }
            else { Message("밝기는 0~100 사이의 정수로 입력하세요."); UpdateMaster(true); }
        }
        private static void AddWheel(C.Slider slider, Action<int> step)
        {
            int accumulated = 0;
            slider.PreviewMouseWheel += delegate(object sender, System.Windows.Input.MouseWheelEventArgs e)
            {
                if (!slider.IsEnabled || System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.None) return;
                if (Math.Sign(accumulated) != Math.Sign(e.Delta)) accumulated = 0;
                accumulated += e.Delta; int ticks = accumulated / 120; accumulated -= ticks * 120;
                if (ticks != 0) step(ticks * 2); e.Handled = true;
            };
        }
        private void UpdateMaster(bool forceNumber = false)
        {
            var targets = MasterTargets();
            int value = targets.Length == 0 ? 0 : targets.Max(m => desired.ContainsKey(m.Id) ? desired[m.Id] : m.Percent);
            updatingMaster = true;
            Find<C.Slider>("MasterBrightness").Value = value;
            Find<C.Slider>("MasterBrightness").IsEnabled = targets.Length > 0 && !suspended;
            Find<C.Button>("MasterMinus").IsEnabled = Find<C.Button>("MasterPlus").IsEnabled = Find<C.TextBox>("MasterNumber").IsEnabled = targets.Length > 0 && !suspended;
            if (forceNumber || !Find<C.TextBox>("MasterNumber").IsKeyboardFocusWithin) Find<C.TextBox>("MasterNumber").Text = value.ToString(CultureInfo.InvariantCulture);
            Find<BrightnessGauge>("MasterGauge").AnimateTo(value); Find<AnimatedPercent>("MasterPercent").AnimateTo(value);
            if (targets.Length == 0) Find<AnimatedPercent>("MasterPercent").Clear();
            Find<C.TextBlock>("MasterHeading").Text = settings.SyncEnabled ? "함께 조절" : "전체 조절";
            updatingMaster = false;
        }
        private MonitorBrightnessPreference[] GroupPreferences()
        {
            return settings.Monitors.Select(p => new MonitorBrightnessPreference { Id = p.Id, Selected = cards.ContainsKey(p.Id) ? cards[p.Id].Selected.IsChecked == true : p.Selected }).ToArray();
        }
        private void SaveLinking()
        {
            if (updatingLink || disposed) return;
            bool enabled = Find<C.CheckBox>("BrightnessSync").IsChecked == true;
            var group = GroupPreferences();
            try
            {
                if (!preview)
                {
                    var saved = Preferences.Read(preferencesPath);
                    saved.Brightness.SyncEnabled = enabled; saved.Brightness.Monitors = group; saved.Write(preferencesPath);
                }
                settings.SyncEnabled = enabled; settings.Monitors = group;
                Message(""); UpdateMaster();
            }
            catch (Exception e)
            {
                updatingLink = true;
                Find<C.CheckBox>("BrightnessSync").IsChecked = settings.SyncEnabled;
                foreach (var pair in cards) pair.Value.Selected.IsChecked = IsSelected(pair.Key);
                updatingLink = false; Message("동기화 설정 저장 실패: " + e.Message);
            }
        }
        private void Queue(string id, int percent)
        {
            if (disposed || suspended || !cards.ContainsKey(id) || !cards[id].Monitor.Supported) return;
            desired[id] = pending[id] = Math.Max(0, Math.Min(100, percent));
            UpdateCard(cards[id], desired[id], "");
            debounce.Stop(); debounce.Start();
        }
        private async Task Flush()
        {
            if (disposed || suspended || busy || pending.Count == 0 || preview) return;
            var batch = pending.Select(pair => new DdcChange { Id = pair.Key, Percent = pair.Value }).ToArray(); pending.Clear(); busy = true;
            try
            {
                DdcResponse result = await client.Send(new DdcRequest { Action = "set", Changes = batch });
                if (disposed) return;
                int verified = 0;
                foreach (var change in batch)
                {
                    var item = result.Monitors.FirstOrDefault(m => m.Id == change.Id);
                    Card card; if (!cards.TryGetValue(change.Id, out card)) continue;
                    if (item != null && item.Supported) { card.Monitor.Current = item.Current; card.Monitor.Maximum = item.Maximum; }
                    if (item != null && item.Verified) verified++;
                    if (!pending.ContainsKey(change.Id)) { desired.Remove(change.Id); UpdateCard(card, card.Monitor.Percent, item != null ? (item.Verified ? "" : item.Status) : result.Error); }
                }
                lastRead = DateTime.UtcNow;
                string message = !string.IsNullOrEmpty(result.Error) ? result.Error : verified == batch.Length ? "" : "일부 모니터 적용 실패 · 상태를 확인하세요.";
                Message(message);
                ShowToast(verified == batch.Length ? "모니터 밝기" : "밝기 확인 필요", verified == batch.Length ? string.Join("  ·  ", batch.Select(c => { Card card; return cards.TryGetValue(c.Id, out card) ? "모니터 " + (Array.IndexOf(monitors, card.Monitor) + 1) + "  " + card.Monitor.Percent + "%" : ""; })) : "설정에서 연결 상태를 확인하세요.");
            }
            finally { busy = false; if (!disposed && pending.Count > 0) debounce.Start(); }
        }
        private void UpdateCard(Card card, int percent, string status)
        { card.Updating = true; card.Slider.Value = percent; card.Percent.AnimateTo(percent); card.Status.Text = status; card.Status.Visibility = string.IsNullOrEmpty(status) ? W.Visibility.Collapsed : W.Visibility.Visible; card.Updating = false; UpdateMaster(); }
        private void ShowToast(string title, string value)
        {
            if (preview || disposed) return;
            if (toast == null) toast = new BrightnessToast();
            toast.Display(UiText.Translate(title), UiText.Translate(value));
        }
        private void SetSnapshot(DdcMonitor[] values)
        {
            var draftSelected = cards.ToDictionary(pair => pair.Key, pair => pair.Value.Selected.IsChecked == true);
            monitors = values; cards.Clear(); var container = Find<C.StackPanel>("BrightnessCards"); container.Children.Clear();
            bool firstConfiguration = settings.Monitors.Length == 0;
            var preferences = settings.Monitors.ToList();
            var ordered = monitors.OrderBy(m => m.X).ThenBy(m => m.Y).ToArray();
            foreach (var monitor in ordered)
            {
                var pref = preferences.FirstOrDefault(m => m.Id == monitor.Id);
                if (pref == null) { pref = new MonitorBrightnessPreference { Id = monitor.Id, Selected = firstConfiguration }; if (!string.IsNullOrEmpty(monitor.Id)) preferences.Add(pref); }
                int number = Array.IndexOf(monitors, monitor) + 1;
                string position = ordered.Length == 2 ? (Array.IndexOf(ordered, monitor) == 0 ? "왼쪽 모니터" : "오른쪽 모니터") : "모니터 " + number;
                var card = new Card { Monitor = monitor }; if (!string.IsNullOrEmpty(monitor.Id)) cards[monitor.Id] = card;
                var box = new C.Border { Style = (W.Style)ui.Resources["CardStyle"], Padding = new W.Thickness(24), Margin = new W.Thickness(0, 0, 0, 18), MinHeight = 229 };
                var panel = new C.Grid { MinHeight = 179 }; box.Child = panel; container.Children.Add(box);
                panel.RowDefinitions.Add(new C.RowDefinition { Height = W.GridLength.Auto }); panel.RowDefinitions.Add(new C.RowDefinition()); panel.RowDefinitions.Add(new C.RowDefinition { Height = W.GridLength.Auto });
                var heading = new C.Grid { Margin = new W.Thickness(0, 0, 0, 20) };
                heading.ColumnDefinitions.Add(new C.ColumnDefinition { Width = new W.GridLength(44) }); heading.ColumnDefinitions.Add(new C.ColumnDefinition()); heading.ColumnDefinitions.Add(new C.ColumnDefinition { Width = W.GridLength.Auto });
                bool portrait = monitor.Height > monitor.Width;
                var device = new C.Border { Width = portrait ? 24 : 38, Height = portrait ? 38 : 26, CornerRadius = new W.CornerRadius(3), BorderThickness = new W.Thickness(1), HorizontalAlignment = W.HorizontalAlignment.Left, VerticalAlignment = W.VerticalAlignment.Center };
                device.SetResourceReference(C.Border.BorderBrushProperty, "Muted");
                device.Child = new C.TextBlock { Text = number.ToString(), HorizontalAlignment = W.HorizontalAlignment.Center, VerticalAlignment = W.VerticalAlignment.Center, FontSize = 12 }; heading.Children.Add(device);
                var labels = new C.StackPanel { VerticalAlignment = W.VerticalAlignment.Center, Margin = new W.Thickness(10, 0, 8, 0) };
                var title = Text(position, false); title.FontSize = 17; title.FontWeight = W.FontWeights.SemiBold; labels.Children.Add(title);
                labels.Children.Add(Text(monitor.Name + " · " + (portrait ? "세로" : "가로"), true)); C.Grid.SetColumn(labels, 1); heading.Children.Add(labels);
                card.Selected = new C.CheckBox { IsChecked = draftSelected.ContainsKey(monitor.Id) ? draftSelected[monitor.Id] : pref.Selected, ToolTip = "함께 조절할 모니터", IsEnabled = monitor.Supported, VerticalAlignment = W.VerticalAlignment.Top };
                W.Automation.AutomationProperties.SetName(card.Selected, "모니터 " + number + " 함께 조절"); C.Grid.SetColumn(card.Selected, 2); heading.Children.Add(card.Selected); panel.Children.Add(heading);
                var body = new C.StackPanel(); C.Grid.SetRow(body, 2); panel.Children.Add(body);
                var reading = new C.Grid();
                var connection = Text(monitor.Supported ? "●  DDC/CI" : "DDC/CI 사용 불가", true); connection.VerticalAlignment = W.VerticalAlignment.Bottom; connection.Margin = new W.Thickness(0, 0, 0, 7); reading.Children.Add(connection);
                var digits = new C.StackPanel { Orientation = C.Orientation.Horizontal, HorizontalAlignment = W.HorizontalAlignment.Right };
                card.Percent = new AnimatedPercent { Text = monitor.Supported ? monitor.Percent.ToString() : "—", Value = monitor.Percent, FontSize = 36, FontWeight = W.FontWeights.Light };
                card.Percent.SetResourceReference(C.TextBlock.FontFamilyProperty, "NumberFont"); digits.Children.Add(card.Percent);
                var unit = Text("%", true); unit.VerticalAlignment = W.VerticalAlignment.Bottom; unit.Margin = new W.Thickness(2, 0, 0, 5); digits.Children.Add(unit); reading.Children.Add(digits); body.Children.Add(reading);
                card.Slider = new C.Slider { Minimum = 0, Maximum = 100, Value = monitor.Percent, IsSnapToTickEnabled = true, TickFrequency = 1, SmallChange = 1, LargeChange = 5, IsEnabled = monitor.Supported };
                W.Automation.AutomationProperties.SetName(card.Slider, "모니터 " + number + " 밝기"); body.Children.Add(card.Slider);
                var endpoints = new C.Grid(); var zero = Text("0", true); zero.Margin = new W.Thickness(0); var hundred = Text("100", true); hundred.Margin = new W.Thickness(0); hundred.HorizontalAlignment = W.HorizontalAlignment.Right; endpoints.Children.Add(zero); endpoints.Children.Add(hundred); body.Children.Add(endpoints);
                card.Status = Text(monitor.Supported ? "" : monitor.Status, true); card.Status.Visibility = monitor.Supported ? W.Visibility.Collapsed : W.Visibility.Visible; body.Children.Add(card.Status);
                string nits = monitor.PeakNits > 0 ? "Windows 보고 피크 " + monitor.PeakNits.ToString("0.#") + " nit" : "nit 정보 없음";
                box.ToolTip = monitor.Width + " × " + monitor.Height + " · " + (monitor.Hdr ? "HDR" : "SDR") + " · " + nits;
                if (!string.IsNullOrEmpty(monitor.Capabilities)) { var expander = new C.Expander { Header = "모니터 " + number + " · " + monitor.Name, Margin = new W.Thickness(0, 12, 0, 0) }; expander.SetResourceReference(C.Control.ForegroundProperty, "Text"); expander.Content = Text(monitor.Capabilities, true); Find<C.StackPanel>("DdcDetails").Children.Add(expander); }
                card.Slider.ValueChanged += delegate { if (!card.Updating) QueueAbsolute(monitor.Id, (int)card.Slider.Value); };
                AddWheel(card.Slider, delta => QueueStep(monitor.Id, delta));
                card.Selected.Checked += delegate { SaveLinking(); }; card.Selected.Unchecked += delegate { SaveLinking(); };
                int queued; if (desired.TryGetValue(monitor.Id, out queued)) UpdateCard(card, queued, "");
            }
            settings.Monitors = preferences.ToArray(); UpdateMaster();
            if (monitors.Length == 0) container.Children.Add(new C.Border { Style = (W.Style)ui.Resources["CardStyle"], Child = Text("연결된 모니터가 없습니다.", true) });
        }
        internal bool ApplySettings()
        {
            int step;
            string modifier = new[] { "RightAlt", "LeftAlt", "CtrlAlt", "CtrlShift" }[Math.Max(0, Find<C.ComboBox>("BrightnessModifier").SelectedIndex)];
            int down = Find<C.ComboBox>("BrightnessDown").SelectedIndex + 0x70, up = Find<C.ComboBox>("BrightnessUp").SelectedIndex + 0x70;
            if (!BrightnessBinding.Valid(modifier, down, up)) { ShortcutMessage("서로 다른 F키를 선택하세요. Ctrl+Alt·AltGr의 F9~F12는 공통 단축키로 예약되어 있습니다."); return false; }
            if (!int.TryParse(Find<C.TextBox>("BrightnessStep").Text, out step) || step < 1 || step > 20) { ShortcutMessage("밝기 간격은 1~20%로 입력하세요."); return false; }
            var updated = new BrightnessPreferences { HotkeysEnabled = Find<C.CheckBox>("BrightnessHotkeys").IsChecked == true, Modifier = modifier, DownKey = down, UpKey = up, Step = step, Target = Find<C.ComboBox>("BrightnessTarget").SelectedIndex == 1 ? "Pointer" : "Selected", SyncEnabled = Find<C.CheckBox>("BrightnessSync").IsChecked == true, Monitors = GroupPreferences() };
            string conflict = ValidateBinding == null ? null : ValidateBinding(new BrightnessBinding(updated.HotkeysEnabled, modifier, down, up));
            if (conflict != null) { ShortcutMessage(conflict); return false; }
            try
            {
                if (!preview) { var preferences = Preferences.Read(preferencesPath); preferences.Brightness = updated; preferences.Write(preferencesPath); }
                settings = updated; if (BindingChanged != null) BindingChanged(); ShortcutMessage("저장됨"); return true;
            }
            catch (Exception e) { ShortcutMessage("설정 저장 실패: " + e.Message); return false; }
        }
        internal void VerifyUi(Action<bool, string> check)
        {
            check(preview && cards.Count == 2 && cards.Values.All(c => c.Slider.Minimum == 0 && c.Slider.Maximum == 100), "Preview has two independent brightness sliders and no hardware access");
            check(Find<C.ComboBox>("BrightnessModifier").SelectedIndex == 0 && Find<C.ComboBox>("BrightnessDown").SelectedIndex == 1 && Find<C.ComboBox>("BrightnessUp").SelectedIndex == 2, "Default brightness binding is right Alt F2/F3");
            Find<C.ComboBox>("BrightnessUp").SelectedIndex = 1; check(!ApplySettings(), "UI rejects duplicate brightness keys"); Find<C.ComboBox>("BrightnessUp").SelectedIndex = 2;
            Find<C.TextBox>("BrightnessStep").Text = "0"; check(!ApplySettings(), "UI rejects zero brightness step"); Find<C.TextBox>("BrightnessStep").Text = "5";
            check(!Find<C.StackPanel>("BrightnessCards").Children.Cast<C.Border>().Any(b => ContainsTextBox(b)), "Monitor cards contain no maximum-nit input");
            cards.Values.First().Selected.IsChecked = false;
            SetSnapshot(monitors);
            check(cards.Values.First().Selected.IsChecked == false, "Refreshing brightness preserves group selection");
            cards.Values.First().Selected.IsChecked = true;
            check(ApplySettings() && settings.SyncEnabled, "Brightness synchronization enabled by default");
            var first = monitors[0]; var second = monitors[1];
            first.Current = 40; second.Current = 60; desired.Clear(); pending.Clear();
            QueueDelta(5); check(pending.Count == 2 && pending.Values.All(p => p == 65), "Brightness shortcut queues 40/60 to 65/65");
            QueueDelta(5); check(pending.Values.All(p => p == 70), "Repeated shortcut uses queued common target");
            cards[first.Id].Slider.Value = 52; check(pending.Values.All(p => p == 52) && cards[second.Id].Slider.Value == 52, "Moving either slider synchronizes both values without recursion");
            desired.Clear(); pending.Clear(); first.Current = 40; second.Current = 60;
            QueueStep(second.Id, -5); check(pending.Values.All(p => p == 35), "Card minus uses lower baseline for linked monitors");
            Find<C.CheckBox>("BrightnessSync").IsChecked = false; desired.Clear(); pending.Clear();
            QueueDelta(5); check(pending[first.Id] == 45 && pending[second.Id] == 65, "Disabling sync restores independent shortcut steps");
            pending.Clear(); desired.Clear(); cards[first.Id].Slider.Value = 44;
            check(pending.Count == 1 && pending.ContainsKey(first.Id), "Unlinked slider changes only its monitor");
            Find<C.CheckBox>("BrightnessSync").IsChecked = true; cards[second.Id].Selected.IsChecked = false;
            desired.Clear(); pending.Clear(); QueueAbsolute(first.Id, 51);
            check(pending.Count == 1 && pending.ContainsKey(first.Id), "Unselected monitor is excluded from linked slider");
            cards[second.Id].Selected.IsChecked = true; desired.Clear(); pending.Clear(); debounce.Stop();
            Find<C.TextBox>("MasterNumber").Text = "81"; CommitMaster();
            check(pending.Count == 2 && pending.Values.All(p => p == 81), "Master direct input assigns the same absolute value to both monitors");
            desired.Clear(); pending.Clear();
            Find<C.TextBox>("MasterNumber").Text = "101"; CommitMaster();
            check(pending.Count == 0, "Out-of-range master input queues no hardware writes");
            StepMaster(5);
            check(pending.Count == 2 && pending.Values.All(p => p == 65), "Master plus uses the brighter monitor as the shared baseline");
            pending.Clear(); desired.Clear();
            var master = Find<C.Slider>("MasterBrightness"); master.ApplyTemplate();
            double original = master.Value;
            SliderFeedback.Update(master, true); SliderFeedback.Update(master, false);
            check(master.Value == original && pending.Count == 0, "Slider hover feedback changes no brightness values or pending writes");
            cards[first.Id].Selected.IsChecked = false; cards[second.Id].Selected.IsChecked = false;
            check(!master.IsEnabled && Find<AnimatedPercent>("MasterPercent").Text == "—", "An empty monitor group disables master adjustment and clears the animated readout");
            cards[first.Id].Selected.IsChecked = true; cards[second.Id].Selected.IsChecked = true;
            desired.Clear(); pending.Clear(); debounce.Stop();
            SetSnapshot(monitors); Message("");
        }
        private static bool ContainsTextBox(W.DependencyObject root)
        { if (root is C.TextBox) return true; for (int i = 0; i < M.VisualTreeHelper.GetChildrenCount(root); i++) if (ContainsTextBox(M.VisualTreeHelper.GetChild(root, i))) return true; return false; }
        public void Dispose() { disposed = true; debounce.Dispose(); client.Dispose(); if (toast != null) toast.Dispose(); }
    }
    internal sealed class BrightnessToast : PassiveWindow
    {
        private const TextFormatFlags TextFlags = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
        private string heading = "", value = "";
        private Font headingFont, valueFont;
        private Rectangle headingBounds, valueBounds, workArea;
        private bool arranging;
        private readonly Timer hide = new Timer { Interval = 1600 };
        internal BrightnessToast() : base(new Rectangle(0, 0, 340, 78), Color.FromArgb(32, 32, 32))
        {
            DoubleBuffered = true;
            hide.Tick += delegate { Hide(); hide.Stop(); };
        }
        internal void Display(string heading, string value)
        {
            Native.Point cursor;
            if (!Native.GetCursorPos(out cursor)) return;
            Rectangle area = Screen.FromPoint(new Point(cursor.X, cursor.Y)).WorkingArea;
            this.heading = heading ?? ""; this.value = value ?? ""; workArea = area;
            // Move the HWND onto the target monitor before querying its effective DPI.
            // Fixed pixel Label heights clipped Hangul when the font scaled at 150%+.
            Location = new Point(area.X + (area.Width - Width) / 2, area.Bottom - Height - 32);
            Arrange(heading, value, Math.Max(96, (int)GetDpiForWindow(Handle)), area);
            Show(); hide.Stop(); hide.Start();
        }
        internal void Arrange(string title, string detail, int dpi, Rectangle area)
        {
            if (arranging) return;
            arranging = true;
            try
            {
                heading = title ?? ""; value = detail ?? ""; workArea = area;
                float scale = dpi / 96f;
                // Pixel fonts make measurement and painting independent of the DC's
                // DPI. We scale them once using the actual destination monitor.
                if (headingFont != null) headingFont.Dispose();
                if (valueFont != null) valueFont.Dispose();
                headingFont = new Font("Segoe UI", 11f * 96f / 72f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
                valueFont = new Font("Segoe UI", 10f * 96f / 72f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                int padding = (int)Math.Ceiling(16 * scale), vertical = (int)Math.Ceiling(12 * scale);
                int gap = (int)Math.Ceiling(5 * scale), safety = (int)Math.Ceiling(3 * scale);
                int available = Math.Max(1, area.Width - 2 * padding);
                int maxWidth = Math.Min(available, (int)Math.Ceiling(560 * scale));
                int natural = Math.Max(TextRenderer.MeasureText(heading, headingFont).Width, TextRenderer.MeasureText(value, valueFont).Width) + 2 * padding;
                int width = Math.Min(maxWidth, Math.Max((int)Math.Ceiling(340 * scale), natural));
                int content = Math.Max(1, width - 2 * padding);
                var proposal = new Size(content, int.MaxValue);
                int titleHeight = TextRenderer.MeasureText(heading, headingFont, proposal, TextFlags).Height + safety;
                int detailHeight = TextRenderer.MeasureText(value, valueFont, proposal, TextFlags).Height + safety;
                headingBounds = new Rectangle(padding, vertical, content, titleHeight);
                valueBounds = new Rectangle(padding, headingBounds.Bottom + gap, content, detailHeight);
                ClientSize = new Size(width, valueBounds.Bottom + vertical);
                Location = new Point(area.X + (area.Width - Width) / 2, Math.Max(area.Top, area.Bottom - Height - (int)Math.Ceiling(32 * scale)));
                Invalidate();
            }
            finally { arranging = false; }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (headingFont == null) return;
            TextRenderer.DrawText(e.Graphics, heading, headingFont, headingBounds, Color.White, BackColor, TextFlags);
            TextRenderer.DrawText(e.Graphics, value, valueFont, valueBounds, Color.FromArgb(96, 205, 255), BackColor, TextFlags);
        }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x02E0 && !arranging && workArea.Width > 0) // WM_DPICHANGED
                Arrange(heading, value, (int)(m.WParam.ToInt64() & 0xffff), workArea);
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);
        protected override void Dispose(bool disposing)
        {
            if (disposing) { hide.Dispose(); if (headingFont != null) headingFont.Dispose(); if (valueFont != null) valueFont.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
