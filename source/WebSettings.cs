using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using M = System.Windows.Media;

namespace MonitorShade
{
    internal sealed partial class SettingsForm
    {
        private WebUi webUi;
        private bool webGlass;
        private string webLicenses;
        private static readonly string[] WebChecks = { "BrightnessSync", "IgnoreWallpaper", "ManualEnabled", "LayoutEnabled", "BrightnessHotkeys", "UseBackdrop", "AutoStart", "Seconds", "Minutes" };
        private static readonly string[] WebNumbers = { "Amount", "CenterPercent", "BrightnessStep" };
        private static readonly string[] WebCombos = { "ManualFirst", "ManualSecond", "BrightnessModifier", "BrightnessDown", "BrightnessUp", "BrightnessTarget", "AppearanceMode", "KoreanFont", "Language" };
        private static readonly string[] WebButtons = { "Start", "Exit", "Refresh", "ApplyManual", "ApplyLayout", "ApplyCenter", "ApplyBrightness", "RefreshBrightness", "Diagnose", "TestLayout", "BrightnessCapabilities", "MasterMinus", "MasterPlus" };
        private static readonly string[] WebMessages = { "Status", "Validation", "CenterValidation", "CenterStatus", "ManualStatus", "LayoutStatus", "BrightnessStatus", "ShortcutsStatus", "AppearanceStatus", "KoreanFontStatus", "HelpStatus", "DdcStatus" };
        internal WebUi BrowserUi { get { return webUi; } }
        private void InitializeWebUi()
        {
            webLicenses = Find<TextBlock>("FontLicenses").Text;
            foreach (string name in new[] { "WebView2-LICENSE.txt", "WebView2-NOTICE.txt" })
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("MonitorShade.Licenses." + name))
                using (var reader = new System.IO.StreamReader(stream)) webLicenses += "\n\n" + name + "\n\n" + reader.ReadToEnd();
            webUi = new WebUi(this, WebState, WebCommand);
            Content = webUi;
        }
        private static string Css(M.Color c) { return c.R + "," + c.G + "," + c.B; }
        internal object WebState()
        {
            double hour = previewHour ?? appearance.Hour(DateTime.Now);
            var p = DayPalette.At(hour);
            var fields = new Dictionary<string, object>();
            foreach (string name in WebChecks) fields[name] = Find<ToggleButton>(name).IsChecked == true;
            foreach (string name in WebNumbers) fields[name] = Find<TextBox>(name).Text;
            foreach (string name in WebCombos) {
                var combo = Find<ComboBox>(name);
                fields[name] = new { value = combo.SelectedIndex, options = combo.Items.Cast<object>().Select(x => x.ToString()).ToArray() };
            }
            var messages = new Dictionary<string, string>();
            foreach (string name in WebMessages) { var text = Find<TextBlock>(name); messages[name] = text.Visibility == Visibility.Collapsed ? "" : text.Text; }
            var enabled = WebButtons.ToDictionary(name => name, name => Find<Button>(name).IsEnabled);
            bool highContrast = SystemParameters.HighContrast;
            webUi.PaintResizeBand(p.Background, webGlass, highContrast);
            return new {
                version = "1.18.1", language = appearance.Language, translations = UiText.English,
                appliedCenterPercent = appliedCenterPercent,
                layoutShortcuts = draftLayoutShortcuts.Select(b => new { action = b.Action, modifiers = b.Modifiers, key = b.Key }).ToArray(),
                appliedShortcuts = appliedLayoutShortcuts.Select(b => new { action = b.Action, modifiers = b.Modifiers, key = b.Key }).ToArray(), preview = preview, glass = webGlass, highContrast = highContrast,
                maximized = WindowState == WindowState.Maximized,
                palette = new { background = Css(p.Background), panel = Css(p.Panel), raised = Css(p.Raised), line = Css(p.Line), soft = Css(p.Soft), accent = Css(p.Accent), ink = Css(p.Ink), text = Css(p.Text), muted = Css(p.Muted), aura = Css(p.Aura), light = p.Light },
                font = appearance.KoreanFont, time = Find<TextBlock>("ThemeTime").Text,
                fields = fields, messages = messages, enabled = enabled,
                monitors = brightnessPage.WebSnapshot(),
                displays = displays.Items.Cast<DisplayChoice>().Select(c => new { id = c.Screen.DeviceName, number = c.Number, selected = displays.SelectedItems.Contains(c), x = c.Screen.Bounds.X, y = c.Screen.Bounds.Y, width = c.Screen.Bounds.Width, height = c.Screen.Bounds.Height, primary = c.Screen.Primary }).ToArray(),
                master = new { percent = (int)Find<Slider>("MasterBrightness").Value, enabled = Find<Slider>("MasterBrightness").IsEnabled },
                licenses = webLicenses
            };
        }
        private static string MessageString(Dictionary<string, object> m, string key)
        { object value; if (!m.TryGetValue(key, out value) || !(value is string) || ((string)value).Length > 256) throw new ArgumentException("올바르지 않은 요청입니다."); return (string)value; }
        private static int MessageInt(Dictionary<string, object> m, string key)
        { object value; int number; if (!m.TryGetValue(key, out value) || !int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) throw new ArgumentException("정수 값이 필요합니다."); return number; }
        private static bool MessageBool(Dictionary<string, object> m, string key)
        { object value; if (!m.TryGetValue(key, out value) || !(value is bool)) throw new ArgumentException("켜짐 또는 꺼짐 값이 필요합니다."); return (bool)value; }
        internal void WebCommand(Dictionary<string, object> m)
        {
            string action = MessageString(m, "action");
            if (action == "layoutShortcut") {
                string name = MessageString(m, "name"); int modifiers = MessageInt(m, "modifiers"), key = MessageInt(m, "key");
                if (!LayoutBindings.Actions.Contains(name) || modifiers < 1 || modifiers > 15 || modifiers == 4 || !LayoutBindings.ValidKey(key)) throw new ArgumentException("Ctrl, Alt 또는 Win이 포함된 키 조합을 선택하세요.");
                var draft = LayoutBindings.Copy(draftLayoutShortcuts); var binding = draft.First(b => b.Action == name); binding.Modifiers = modifiers; binding.Key = key; draftLayoutShortcuts = draft;
                Find<TextBlock>("LayoutStatus").Text = LayoutBindings.Error(draft, layoutEnabled.IsChecked == true ? brightnessPage.ConfiguredBinding : null) ?? "저장을 누르면 단축키를 저장합니다.";
                return;
            }
            if (action == "resetLayoutShortcuts") { draftLayoutShortcuts = LayoutBindings.Defaults(); Find<TextBlock>("LayoutStatus").Text = "저장을 누르면 단축키를 저장합니다."; return; }
            if (action == "caption") {
                string op = MessageString(m, "value");
                if (op == "minimize") SystemCommands.MinimizeWindow(this);
                else if (op == "maximize") { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
                else if (op == "close") Close();
                else throw new ArgumentException("알 수 없는 창 명령입니다.");
                return;
            }
            if (action == "field") {
                string name = MessageString(m, "name");
                if (WebChecks.Contains(name)) Find<ToggleButton>(name).IsChecked = MessageBool(m, "value");
                else if (WebNumbers.Contains(name)) {
                    string value = MessageString(m, "value");
                    if (value.Length > 5 || value.Any(c => c < '0' || c > '9')) throw new ArgumentException("숫자만 입력하세요.");
                    Find<TextBox>(name).Text = value;
                }
                else if (WebCombos.Contains(name)) {
                    var combo = Find<ComboBox>(name); int value = MessageInt(m, "value");
                    if (value < 0 || value >= combo.Items.Count) throw new ArgumentOutOfRangeException("value");
                    combo.SelectedIndex = value;
                }
                else throw new ArgumentException("알 수 없는 설정입니다.");
                return;
            }
            if (action == "button") {
                string name = MessageString(m, "name");
                if (!WebButtons.Contains(name)) throw new ArgumentException("알 수 없는 명령입니다.");
                var button = Find<Button>(name);
                if (!button.IsEnabled) throw new InvalidOperationException("설정값을 먼저 확인하세요.");
                if (preview && new[] { "Start", "Diagnose", "TestLayout" }.Contains(name)) { status.Text = "미리보기 · 실제 화면과 설정은 변경하지 않습니다."; return; }
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return;
            }
            if (action == "brightness" || action == "step") { brightnessPage.WebBrightness(MessageString(m, "id"), MessageInt(m, "value"), action == "step"); return; }
            if (action == "linked") { brightnessPage.WebSelected(MessageString(m, "id"), MessageBool(m, "value")); return; }
            if (action == "display") {
                string id = MessageString(m, "id"); var choice = displays.Items.Cast<DisplayChoice>().FirstOrDefault(c => c.Screen.DeviceName == id);
                if (choice == null) throw new ArgumentException("연결된 모니터가 아닙니다.");
                if (MessageBool(m, "value")) { if (!displays.SelectedItems.Contains(choice)) displays.SelectedItems.Add(choice); } else displays.SelectedItems.Remove(choice);
                return;
            }
            if (action == "page") {
                string page = MessageString(m, "value");
                if (!new[] { "Brightness", "Protection", "Layout", "Shortcuts", "General", "Help" }.Contains(page)) throw new ArgumentException("알 수 없는 페이지입니다.");
                if (page == "Brightness") RefreshWebBrightness();
                return;
            }
            throw new ArgumentException("허용되지 않은 요청입니다.");
        }
        private async void RefreshWebBrightness() { await brightnessPage.Refresh(false); }
    }
}
