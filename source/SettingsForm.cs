using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Microsoft.Win32;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;
using I = System.Windows.Media.Imaging;

namespace MonitorShade
{
    internal static class StartupRegistration
    {
        private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        internal static string Command(string executable) { return "\"" + Path.GetFullPath(executable) + "\" --autostart"; }
        internal static bool Enabled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key != null && string.Equals(key.GetValue("MonitorShade") as string, Command(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase);
            }
        }
        internal static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue("MonitorShade", Command(Application.ExecutablePath), RegistryValueKind.String);
                else key.DeleteValue("MonitorShade", false);
            }
        }
        internal static Screen FindTarget(string name) { return string.IsNullOrWhiteSpace(name) ? null : Screen.AllScreens.FirstOrDefault(s => s.DeviceName == name); }
    }
    internal sealed class DisplayChoice
    {
        internal ConnectedDisplay Screen;
        public string Number { get; set; }
        public string Label { get { return "모니터 " + Number; } }
        public override string ToString() { return "디스플레이 " + Number + (Screen.Primary ? " (기본)" : ""); }
    }

    internal sealed partial class SettingsForm : SettingsWindow
    {
        private readonly WatchGroup watcher = new WatchGroup();
        private readonly DisplayRecovery displayRecovery;
        private readonly DisplaySelection displaySelection;
        private readonly Timer reconnectTimer = new Timer { Interval = 2000 };
        private ConnectedDisplay[] connectedDisplays = new ConnectedDisplay[0];
        private bool refreshingDisplays, haveTopology;
        private string topologyKey = "";
        private int savedSelectionRevision = -1, disconnectedCount;
        private readonly bool preview;
        private readonly string preferencesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MonitorShade.settings.xml");
        private readonly C.UserControl ui;
        private readonly C.ListBox displays;
        private readonly C.TextBox amount;
        private readonly C.RadioButton seconds, minutes;
        private readonly C.TextBlock status, shortcut, validation, detail;
        private readonly C.Canvas map;
        private readonly C.Button start;
        private readonly C.CheckBox autoStart;
        private readonly C.CheckBox ignoreWallpaper;
        private readonly C.CheckBox layoutEnabled;
        private readonly C.TextBox centerPercent;
        private readonly WindowArranger arranger = new WindowArranger();
        private LayoutKeyboardHook layoutHook;
        private readonly BrightnessPage brightnessPage;
        private readonly ManualShadePage manualShade;
        private bool appliedLayoutEnabled;
        private int appliedCenterPercent;
        private LayoutShortcut[] appliedLayoutShortcuts, draftLayoutShortcuts;
        private readonly Timer startupTimer = new Timer { Interval = 1000 };
        private bool startupPending, updatingStartup;
        private string[] savedStartupTargets;
        private int startupAttempts;
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem pauseItem;
        private bool exiting, hotkey, settingsHotkey, diagnosing, dark;
        private bool sessionBlocked, powerBlocked;
        private readonly Timer appearanceTimer = new Timer { Interval = 30000 };
        private PageTransition pageTransition;
        private readonly CompositedBackdrop backdrop;
        private bool suppressMotion;
        private readonly Icon trayIcon;
        private AppearancePreferences appearance;
        private double? previewHour;
        private bool SystemSuspended { get { return sessionBlocked || powerBlocked; } }
        private readonly uint settingsMessage = Native.RegisterWindowMessage("MonitorShade.ShowSettings.v1");

        public SettingsForm(bool previewMode, bool automatic = true, bool legacyPreview = false)
        {
            preview = previewMode;
            displayRecovery = new DisplayRecovery(watcher);
            reconnectTimer.Tick += delegate { ReconcileDisplays(); };
            Text = "MonitorShade";
            Width = 1120; Height = 840; MinWidth = 960; MinHeight = 640;
            WindowStartupLocation = W.WindowStartupLocation.CenterScreen;
            Background = M.Brushes.Transparent;
            trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            Icon = W.Interop.Imaging.CreateBitmapSourceFromHIcon(trayIcon.Handle, W.Int32Rect.Empty, I.BitmapSizeOptions.FromEmptyOptions());
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MonitorShade.Settings.xaml"))
                ui = (C.UserControl)W.Markup.XamlReader.Load(stream);
            displays = Find<C.ListBox>("Displays"); amount = Find<C.TextBox>("Amount");
            seconds = Find<C.RadioButton>("Seconds"); minutes = Find<C.RadioButton>("Minutes");
            status = Find<C.TextBlock>("Status"); shortcut = Find<C.TextBlock>("Shortcut");
            validation = Find<C.TextBlock>("Validation");
            detail = Find<C.TextBlock>("DisplayDetail"); map = Find<C.Canvas>("Map"); start = Find<C.Button>("Start");
            autoStart = Find<C.CheckBox>("AutoStart");
            ignoreWallpaper = Find<C.CheckBox>("IgnoreWallpaper");
            layoutEnabled = Find<C.CheckBox>("LayoutEnabled"); centerPercent = Find<C.TextBox>("CenterPercent");
            // The old capture compositor exists only for historical WPF regression tools.
            // Production uses a transparent Chromium surface over native DWM Acrylic.
            if (legacyPreview)
                backdrop = new CompositedBackdrop(this, ui, () => SystemSuspended, delegate {
                    Dispatcher.BeginInvoke(new Action(delegate { if (!IsDisposed && appearance != null) ApplyTheme(null); }));
                });
            Content = legacyPreview ? backdrop.Root : null;
            ConnectCaption(ui);
            pageTransition = new PageTransition(Find<C.Grid>("PageHost"), new[] { "Protection", "Layout", "Brightness", "Shortcuts", "General", "Help" }.ToDictionary(name => name, name => Find<C.ScrollViewer>(name + "Page")));
            foreach (string name in new[] { "Protection", "Layout", "Brightness", "Shortcuts", "General", "Help" })
                M.RenderOptions.SetClearTypeHint((W.DependencyObject)Find<C.ScrollViewer>(name + "Page").Content, M.ClearTypeHint.Enabled);
            StateChanged += delegate { if (IsHandleCreated) BeginInvoke(new Action(delegate { if (!IsDisposed) ApplyTheme(null); })); };

            Preferences preferences = File.Exists(preferencesPath) ? Preferences.Read(preferencesPath) : new Preferences { WaitValue = 120, Minutes = false };
            watchEnabled = preferences.WatchEnabled;
            Find<C.CheckBox>("WatchEnabled").IsChecked = watchEnabled;
            startupPending = watchEnabled && !preview;
            appearance = preferences.Appearance; appearance.Normalize(); ApplyTheme(null);
            if (!File.Exists(preferencesPath)) preferences.DeviceNames = Screen.AllScreens.Select(s => s.DeviceName).ToArray();
            displaySelection = new DisplaySelection(preferences);
            brightnessPage = new BrightnessPage(ui, preview ? new BrightnessPreferences() : preferences.Brightness, preferencesPath, preview);
            manualShade = new ManualShadePage(ui, preview ? new ManualShadePreferences() : preferences.ManualShade, preferencesPath, preview, delegate { return Handle; }, watcher);
            brightnessPage.BindingChanged += delegate { SetLayoutHook(); };
            appliedLayoutShortcuts = LayoutBindings.Normalize(preferences.LayoutShortcuts);
            draftLayoutShortcuts = LayoutBindings.Copy(appliedLayoutShortcuts);
            brightnessPage.ValidateBinding = binding => appliedLayoutEnabled ? LayoutBindings.Error(appliedLayoutShortcuts, binding) : null;
            appliedLayoutEnabled = preferences.LayoutEnabled; appliedCenterPercent = preferences.CenterPercent;
            layoutEnabled.IsChecked = appliedLayoutEnabled; centerPercent.Text = appliedCenterPercent.ToString(CultureInfo.InvariantCulture);
            centerPercent.TextChanged += delegate { ValidateLayout(); };
            centerPercent.PreviewTextInput += delegate(object sender, System.Windows.Input.TextCompositionEventArgs e) { e.Handled = e.Text.Any(c => c < '0' || c > '9'); };
            Find<C.RadioButton>("NavProtection").Checked += delegate { SelectPage("Protection"); };
            Find<C.RadioButton>("NavLayout").Checked += delegate { SelectPage("Layout"); };
            Find<C.RadioButton>("NavGeneral").Checked += delegate { SelectPage("General"); };
            Find<C.RadioButton>("NavHelp").Checked += delegate { SelectPage("Help"); };
            Find<C.RadioButton>("NavShortcuts").Checked += delegate { SelectPage("Shortcuts"); };
            Find<C.RadioButton>("NavBrightness").Checked += async delegate { SelectPage("Brightness"); await brightnessPage.Refresh(false); };
            Find<C.Button>("ApplyLayout").Click += delegate { ApplyLayoutSettings(true); };
            Find<C.Button>("ApplyCenter").Click += delegate { ApplyCenterSettings(); };
            Find<C.Button>("TestLayout").Click += delegate { DiagnoseLayout(); };
            savedStartupTargets = preferences.SelectedDevices();
            if (!File.Exists(preferencesPath)) savedStartupTargets = Screen.AllScreens.Select(s => s.DeviceName).ToArray();
            amount.Text = preferences.WaitValue.ToString(CultureInfo.InvariantCulture);
            minutes.IsChecked = preferences.Minutes; seconds.IsChecked = !preferences.Minutes;
            ignoreWallpaper.IsChecked = preferences.IgnoreWallpaper;
            displays.SelectionChanged += delegate
            {
                if (!refreshingDisplays) { displaySelection.Edit(connectedDisplays, CurrentDevices()); QueueProtectionChanges(); }
                UpdateDisplay();
            };
            map.SizeChanged += delegate { DrawMap(); };
            RefreshDisplays();
            amount.TextChanged += delegate { ValidateWait(); QueueProtectionChanges(); };
            seconds.Checked += delegate { ValidateWait(); QueueProtectionChanges(); };
            minutes.Checked += delegate { ValidateWait(); QueueProtectionChanges(); };
            ignoreWallpaper.Checked += delegate { QueueProtectionChanges(); };
            ignoreWallpaper.Unchecked += delegate { QueueProtectionChanges(); };
            amount.PreviewTextInput += delegate(object sender, System.Windows.Input.TextCompositionEventArgs e) { e.Handled = e.Text.Any(c => c < '0' || c > '9'); };
            start.Click += delegate { StartWatching(); };
            Find<C.Button>("Exit").Click += delegate { ExitApplication(); };
            Find<C.Button>("Refresh").Click += delegate { RefreshDisplays(true); if (displayRecovery.Enabled) ReconcileDisplays(); };
            Find<C.Button>("Diagnose").Click += delegate { Diagnose(); };
            try { autoStart.IsChecked = StartupRegistration.Enabled; } catch { autoStart.IsChecked = false; }
            autoStart.Checked += delegate { ChangeStartup(true); };
            autoStart.Unchecked += delegate { ChangeStartup(false); };
            ValidateWait();

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("설정 열기", null, delegate { OpenSettings(); });
            pauseItem = new ToolStripMenuItem("감시 시작 / 다시 시작", null, delegate { Toggle(); });
            menu.Items.Add(pauseItem); menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, delegate { ExitApplication(); });
            tray = new NotifyIcon { Icon = trayIcon, Text = "MonitorShade · 준비됨", ContextMenuStrip = menu, Visible = !preview };
            tray.DoubleClick += delegate { OpenSettings(); };
            arranger.Failed += delegate(string reason) { Find<C.TextBlock>("LayoutStatus").Text = reason; tray.ShowBalloonTip(3000, UiText.Translate("MonitorShade 창 배치"), UiText.Translate(reason), ToolTipIcon.Info); };
            watcher.StateChanged += UpdateWatchingStatus;
            manualShade.Controller.Changed += UpdateWatchingStatus;
            watcher.Failed += delegate(string reason) { UpdateWatchingStatus(); status.Text = watcher.RunningCount + "대 감시 · 일부 중지"; tray.ShowBalloonTip(4000, UiText.Translate("MonitorShade 일부 감시 중지"), UiText.Translate(reason), ToolTipIcon.Warning); };
            if (!preview)
            {
                SystemEvents.DisplaySettingsChanged += DisplayChanged;
                SystemEvents.SessionSwitch += SessionChanged;
                SystemEvents.PowerModeChanged += PowerChanged;
                SystemEvents.UserPreferenceChanged += ThemeChanged;
            }
            InitializeAppearance();
            appearanceTimer.Tick += delegate { if (IsVisible) ApplyTheme(null); };
            IsVisibleChanged += delegate { if (IsVisible) { ApplyTheme(null); appearanceTimer.Start(); } else { appearanceTimer.Stop(); pageTransition.Finish(); } };
            ValidateLayout();
            if (!preview) { IntPtr messageWindow = Handle; SetLayoutHook(); manualShade.Register(); }
            if (!preview) BeginInvoke(new Action(async delegate { await brightnessPage.Refresh(false); }));
            InitializeProtection();
            if (startupPending)
            {
                IntPtr window = Handle; // Hidden message window for tray, hotkey and startup retry timer.
                status.Text = "자동 감시 준비 중";
                startupTimer.Tick += delegate { TryAutomaticStart(); };
                startupTimer.Start();
            }
            Find<C.RadioButton>("NavBrightness").IsChecked = true;
            if (!legacyPreview) InitializeWebUi();
        }

        private void CancelAutomaticStart() { startupPending = false; startupTimer.Stop(); }
        private void TryAutomaticStart()
        {
            if (!startupPending) return;
            startupAttempts++;
            if (startupAttempts < 3) return;
            try
            {
                RefreshDisplays();
                if (!StartWatching(true)) throw new InvalidOperationException("대기 시간 설정을 확인해 주세요.");
                CancelAutomaticStart();
            }
            catch (Exception e)
            {
                if (startupAttempts < 30) return;
                CancelAutomaticStart(); Pause(e.Message);
                tray.ShowBalloonTip(4000, UiText.Translate("MonitorShade 자동 시작 확인"), UiText.Translate(e.Message), ToolTipIcon.Warning);
            }
        }
        private void ChangeStartup(bool enable)
        {
            if (updatingStartup || preview) return;
            try
            {
                if (enable)
                {
                    int delay;
                    if (!TryGetSeconds(out delay) || displays.SelectedItems.Count == 0) throw new InvalidOperationException("모니터와 대기 시간을 먼저 설정해 주세요.");
                    SavePreferences();
                }
                StartupRegistration.SetEnabled(enable);
                status.Text = enable ? "로그인 시 자동 실행 켜짐" : "자동 실행 꺼짐";
            }
            catch (Exception e)
            {
                updatingStartup = true;
                try { autoStart.IsChecked = StartupRegistration.Enabled; } catch { autoStart.IsChecked = false; }
                updatingStartup = false;
                MessageBox.Show(this, UiText.Translate(e.Message), UiText.Translate("자동 실행 설정"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        private void SavePreferences()
        {
            Preferences saved = Preferences.Read(preferencesPath);
            displaySelection.Store(saved);
            saved.WaitValue = int.Parse(amount.Text, CultureInfo.InvariantCulture); saved.Minutes = minutes.IsChecked == true; saved.IgnoreWallpaper = ignoreWallpaper.IsChecked == true; saved.WatchEnabled = watchEnabled; saved.LayoutEnabled = appliedLayoutEnabled; saved.CenterPercent = appliedCenterPercent; saved.LayoutShortcuts = LayoutBindings.Copy(appliedLayoutShortcuts); saved.Write(preferencesPath);
            savedSelectionRevision = displaySelection.Revision;
        }

        private void SelectPage(string page)
        {
            pageTransition.Select(page, !suppressMotion);
        }
        private bool TryGetPercent(out int value) { return int.TryParse(centerPercent.Text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 10 && value <= 100; }
        private void ValidateLayout()
        {
            int value; bool valid = TryGetPercent(out value);
            Find<C.TextBlock>("CenterValidation").Visibility = valid ? W.Visibility.Collapsed : W.Visibility.Visible;
            Find<C.Button>("ApplyCenter").IsEnabled = valid;
            if (amount != null && seconds != null && start != null) ValidateWait();
        }
        private void SetLayoutHook()
        {
            if (preview) return;
            try
            {
                if (!SystemSuspended && (appliedLayoutEnabled || brightnessPage.Binding.Enabled) && layoutHook == null)
                    layoutHook = new LayoutKeyboardHook(delegate(IntPtr window, LayoutDirection direction) { Dispatch(delegate { if (appliedLayoutEnabled && !diagnosing && !SystemSuspended && LayoutNative.GetForegroundWindow() == window) arranger.Apply(window, direction, appliedCenterPercent); }); }, delegate(int direction) { Dispatch(delegate { if (!diagnosing && !SystemSuspended) brightnessPage.Shortcut(direction); }); });
                if (layoutHook != null) layoutHook.ConfigureLayout(appliedLayoutShortcuts);
                if (layoutHook != null) layoutHook.Enabled = appliedLayoutEnabled && !diagnosing && !SystemSuspended;
                if (layoutHook != null) layoutHook.ConfigureBrightness(diagnosing || SystemSuspended ? null : brightnessPage.Binding);
                if (!appliedLayoutEnabled) arranger.Reset();
                Find<C.TextBlock>("LayoutStatus").Text = "";
            }
            catch (Exception e) { Find<C.TextBlock>("LayoutStatus").Text = "단축키를 시작할 수 없습니다: " + e.Message; }
        }
        private bool ApplyCenterSettings()
        {
            int value; if (!TryGetPercent(out value)) { ValidateLayout(); return false; }
            try
            {
                if (!preview) { var saved = Preferences.Read(preferencesPath); saved.CenterPercent = value; saved.Write(preferencesPath); }
                appliedCenterPercent = value; arranger.Reset(); Find<C.TextBlock>("CenterStatus").Text = "저장됨"; return true;
            }
            catch (Exception e) { Find<C.TextBlock>("CenterStatus").Text = "저장하지 못했습니다: " + e.Message; return false; }
        }
        private bool ApplyLayoutSettings(bool shortcutsOnly = false)
        {
            int value = appliedCenterPercent;
            if (!shortcutsOnly && !TryGetPercent(out value)) { ValidateLayout(); return false; }
            string conflict = LayoutBindings.Error(draftLayoutShortcuts, layoutEnabled.IsChecked == true ? brightnessPage.ConfiguredBinding : null);
            if (conflict != null) { Find<C.TextBlock>("LayoutStatus").Text = conflict; return false; }
            try
            {
                if (!preview)
                {
                    Preferences saved = Preferences.Read(preferencesPath);
                    saved.LayoutEnabled = layoutEnabled.IsChecked == true; saved.CenterPercent = value; saved.LayoutShortcuts = LayoutBindings.Copy(draftLayoutShortcuts); saved.Write(preferencesPath);
                }
                appliedLayoutEnabled = layoutEnabled.IsChecked == true; appliedCenterPercent = value;
                appliedLayoutShortcuts = LayoutBindings.Copy(draftLayoutShortcuts);
                arranger.Reset(); SetLayoutHook(); Find<C.TextBlock>("LayoutStatus").Text = "저장됨"; return true;
            }
            catch (Exception e) { Find<C.TextBlock>("LayoutStatus").Text = "저장하지 못했습니다: " + e.Message; return false; }
        }
        private void DiagnoseLayout()
        {
            Pause("창 배치 진단 중"); diagnosing = true; SetLayoutHook();
            try
            {
                Hide();
                string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MonitorShade.layout-diagnostics.txt");
                int result = LayoutVerification.Run(report, true);
                Find<C.TextBlock>("HelpStatus").Text = result == 0 ? "창 배치 진단 통과 · 결과 파일 저장됨" : "창 배치 진단 실패 · 결과 파일 확인";
            }
            finally { diagnosing = false; SetLayoutHook(); Show(); Activate(); }
        }

        private T Find<T>(string name) where T : class { return (T)ui.FindName(name); }
        private M.Brush Brush(string name) { return (M.Brush)ui.Resources[name]; }
        private void InitializeAppearance()
        {
            try
            {
                string licenses = "";
                foreach (string name in new[] { "pretendard-license.txt", "manrope-ofl.txt" })
                {
                    var resource = W.Application.GetResourceStream(new Uri("pack://application:,,,/MonitorShade;component/fonts/" + name));
                    if (resource != null) using (var reader = new StreamReader(resource.Stream)) licenses += reader.ReadToEnd() + Environment.NewLine + Environment.NewLine;
                }
                Find<C.TextBlock>("FontLicenses").Text = licenses;
            }
            catch { Find<C.TextBlock>("FontLicenses").Text = "Pretendard / Manrope — SIL Open Font License 1.1. 라이선스 원문은 EXE의 fonts 리소스에 포함되어 있습니다."; }
            var language = Find<C.ComboBox>("Language");
            language.ItemsSource = new[] { "KO", "EN" }; language.SelectedIndex = appearance.Language == "EN" ? 1 : 0;
            UiText.Language = appearance.Language;
            LocalizeTray();
            var mode = Find<C.ComboBox>("AppearanceMode");
            mode.ItemsSource = new[] { "시간에 따라 자동", "아침 · 09:00", "낮 · 14:00", "저녁 · 20:00", "밤 · 00:00" };
            string[] values = { "Auto", "Morning", "Day", "Evening", "Night" };
            mode.SelectedIndex = Array.IndexOf(values, appearance.Theme);
            Find<C.CheckBox>("UseBackdrop").IsChecked = appearance.Blur;
            var koreanFont = Find<C.ComboBox>("KoreanFont");
            koreanFont.ItemsSource = new[] { "맑은 고딕 · Windows", "Pretendard · 기본" };
            koreanFont.SelectedIndex = appearance.KoreanFont == "Pretendard" ? 1 : 0;
            Action save = delegate
            {
                appearance.Language = language.SelectedIndex == 1 ? "EN" : "KO";
                UiText.Language = appearance.Language; LocalizeTray();
                appearance.Theme = values[Math.Max(0, mode.SelectedIndex)];
                appearance.Blur = Find<C.CheckBox>("UseBackdrop").IsChecked == true;
                appearance.KoreanFont = koreanFont.SelectedIndex == 1 ? "Pretendard" : "MalgunGothic";
                previewHour = null; ApplyTheme(null);
                if (preview) return;
                try { var saved = Preferences.Read(preferencesPath); saved.Appearance = appearance; saved.Write(preferencesPath); }
                catch (Exception e) { Find<C.TextBlock>("AppearanceStatus").Text = "외형 설정 저장 실패: " + e.Message; }
            };
            language.SelectionChanged += delegate { save(); };
            mode.SelectionChanged += delegate { save(); };
            koreanFont.SelectionChanged += delegate { save(); };
            Find<C.CheckBox>("UseBackdrop").Checked += delegate { save(); };
            Find<C.CheckBox>("UseBackdrop").Unchecked += delegate { save(); };
        }
        private void ApplyTheme(bool? forceDark)
        {
            double hour = forceDark.HasValue ? (forceDark.Value ? 20 : 9) : previewHour ?? appearance.Hour(DateTime.Now);
            DayPalette p = DayPalette.At(hour); dark = !p.Light;
            bool composed = backdrop != null && backdrop.Apply(appearance.Blur, dark);
            SetBackdropFrame(appearance.Blur && !composed && DesktopBackdrop.TransparencyEnabled);
            bool nativeGlass = IsHandleCreated && DesktopBackdrop.Apply(Handle, appearance.Blur && !composed, dark);
            bool glass = composed || nativeGlass;
            if (backdrop != null) TextPresentation.Apply(backdrop.Root, glass, composed);
            string[] names = { "Page", "Card", "CardTop", "Map", "Input", "Stroke", "Text", "Muted", "Accent", "AccentText", "Hover", "Monitor", "Selection", "Soft", "TextSurface" };
            // Preserve 1.12's translucent card brushes. Only the final background
            // canvas is RGB; no opaque text patches are added to the design.
            M.Color[] colors = { DayPalette.Alpha(p.Background, (byte)(glass ? 194 : 255)), DayPalette.Alpha(p.Panel, (byte)(glass ? 210 : 255)), DayPalette.Alpha(p.Raised, (byte)(glass ? 210 : 255)), p.Panel, p.Panel, DayPalette.Alpha(p.Line, 180), p.Text, p.Muted, p.Accent, p.Ink, p.Raised, p.Soft, p.Accent, p.Soft, p.Background };
            if (W.SystemParameters.HighContrast)
            {
                for (int i = 0; i < colors.Length; i++) colors[i] = W.SystemColors.WindowColor;
                colors[6] = colors[7] = W.SystemColors.WindowTextColor; colors[8] = colors[12] = W.SystemColors.HighlightColor;
                colors[9] = W.SystemColors.HighlightTextColor; colors[5] = colors[13] = W.SystemColors.GrayTextColor;
            }
            for (int i = 0; i < names.Length; i++)
            {
                var brush = ui.Resources[names[i]] as M.SolidColorBrush;
                if (brush == null || brush.IsFrozen) ui.Resources[names[i]] = new M.SolidColorBrush(colors[i]); else brush.Color = colors[i];
            }
            ui.Resources["AuraColor"] = W.SystemParameters.HighContrast ? M.Colors.Transparent : DayPalette.Alpha(p.Aura, 88);
            ui.Resources["AccentColor"] = colors[8];
            bool hasWindowsFont = TextPresentation.HasWindowsFont;
            ui.FontFamily = TextPresentation.KoreanFamily(appearance.KoreanFont, hasWindowsFont);
            var fontStatus = Find<C.TextBlock>("KoreanFontStatus");
            if (fontStatus != null)
            {
                bool fallback = appearance.KoreanFont == "MalgunGothic" && !hasWindowsFont;
                fontStatus.Text = fallback ? "맑은 고딕이 설치되어 있지 않아 내장 Pretendard를 사용합니다." : "";
                fontStatus.Visibility = fallback ? W.Visibility.Visible : W.Visibility.Collapsed;
            }
            ui.Resources["NumberFont"] = new M.FontFamily(new Uri("pack://application:,,,/"), "./fonts/#Manrope");
            foreach (string name in new[] { "Protection", "Layout", "Brightness", "Shortcuts", "General", "Help" }) NavigationFeedback.Update(Find<C.RadioButton>("Nav" + name), Find<C.RadioButton>("Nav" + name).IsMouseOver, false);
            var time = Find<C.TextBlock>("ThemeTime"); if (time != null) time.Text = TimeSpan.FromHours(hour).ToString(@"hh\:mm") + (appearance.Theme == "Auto" && !forceDark.HasValue ? " · 자동" : "");
            var label = Find<C.TextBlock>("AppearanceStatus");
            if (label != null) label.Text = !appearance.Blur || composed || nativeGlass ? "" : "배경 블러는 Windows 11 22H2 이상에서 투명도 효과가 켜져 있을 때 적용됩니다.";
            webGlass = glass;
            if (map != null) DrawMap();
        }
        private void ApplyWindowStyle()
        {
            ApplyTheme(null);
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e); ApplyWindowStyle();
            if (!preview)
            {
                hotkey = Native.RegisterHotKey(Handle, 1, 0x4000 | 0x0002 | 0x0001, 0x7B);
                settingsHotkey = Native.RegisterHotKey(Handle, 2, 0x4000 | 0x0002 | 0x0001, 0x7A);
                if (!hotkey) shortcut.Text = "일시정지 / 재시작은 트레이 메뉴에서";
                if (manualShade != null) manualShade.Register();
            }
        }
        private string[] CurrentDevices() { return displays.SelectedItems.Cast<DisplayChoice>().Select(c => c.Screen.DeviceName).ToArray(); }
        private void RefreshDisplays(bool force = false)
        {
            connectedDisplays = ConnectedDisplay.Read();
            string[] keep = displaySelection.Resolve(connectedDisplays).Select(d => d.DeviceName).ToArray();
            disconnectedCount = displaySelection.Count - keep.Length;
            string key = string.Join(";", connectedDisplays.OrderBy(d => d.DeviceName).Select(d => d.DeviceName + "|" + d.MonitorId + "|" + d.Bounds + "|" + d.Primary));
            if (!force && haveTopology && topologyKey == key) return;
            bool changed = !haveTopology || topologyKey != key;
            topologyKey = key; haveTopology = true;
            if (changed)
            {
                arranger.Reset();
                if (brightnessPage != null) brightnessPage.Invalidate();
                if (manualShade != null) manualShade.Controller.Clear();
            }
            DisplayChoice[] choices = connectedDisplays.OrderByDescending(s => s.Primary).ThenBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).Select((s, i) => new DisplayChoice { Screen = s, Number = (i + 1).ToString() }).ToArray();
            refreshingDisplays = true;
            try
            {
                displays.ItemsSource = choices;
                foreach (DisplayChoice choice in choices) if (keep.Contains(choice.Screen.DeviceName)) displays.SelectedItems.Add(choice);
            }
            finally { refreshingDisplays = false; }
            UpdateDisplay();
            if (manualShade != null) manualShade.RefreshTargets();
        }
        private void UpdateDisplay()
        {
            detail.Text = displays.SelectedItems.Count == 0 ? "모니터를 선택하세요." : displays.SelectedItems.Count + "대 선택";
            DrawMap();
            if (start != null && seconds != null && amount != null) ValidateWait();
        }
        private void DrawMap()
        {
            if (map == null || displays == null || map.ActualWidth < 1) return;
            map.Children.Clear();
            DisplayChoice[] choices = displays.Items.Cast<DisplayChoice>().ToArray();
            if (choices.Length == 0) return;
            Rectangle all = choices.Select(c => c.Screen.Bounds).Aggregate(Rectangle.Union);
            double scale = Math.Min((map.ActualWidth - 40) / all.Width, (map.ActualHeight - 24) / all.Height);
            foreach (DisplayChoice choice in choices)
            {
                Rectangle b = choice.Screen.Bounds;
                bool selected = CurrentDevices().Contains(choice.Screen.DeviceName);
                C.Button button = new C.Button
                {
                    Width = Math.Max(32, b.Width * scale - 4), Height = Math.Max(32, b.Height * scale - 4),
                    Background = Brush(selected ? "Selection" : "Monitor"), Foreground = Brush(selected ? "AccentText" : "Text"),
                    BorderBrush = selected ? Brush("Accent") : M.Brushes.Transparent,
                    BorderThickness = new W.Thickness(selected ? 2 : 0), Padding = new W.Thickness(0),
                    Content = new C.TextBlock { Text = choice.Number, FontSize = 28, FontWeight = W.FontWeights.SemiBold },
                    ToolTip = choice.ToString() + " · " + b.Width + " × " + b.Height
                };
                W.Automation.AutomationProperties.SetName(button, choice.ToString() + (selected ? " 선택됨" : " 선택"));
                button.Click += delegate { if (displays.SelectedItems.Contains(choice)) displays.SelectedItems.Remove(choice); else displays.SelectedItems.Add(choice); };
                C.Canvas.SetLeft(button, (map.ActualWidth - all.Width * scale) / 2 + (b.X - all.X) * scale);
                C.Canvas.SetTop(button, (map.ActualHeight - all.Height * scale) / 2 + (b.Y - all.Y) * scale);
                map.Children.Add(button);
            }
        }
        private bool TryGetSeconds(out int value)
        {
            int number; value = 0;
            if (!int.TryParse(amount.Text, NumberStyles.None, CultureInfo.InvariantCulture, out number)) return false;
            int maximum = minutes.IsChecked == true ? 1440 : 86400;
            if (number < 1 || number > maximum) return false;
            value = number * (minutes.IsChecked == true ? 60 : 1); return true;
        }
        private void ValidateWait()
        {
            int delay;
            bool valid = TryGetSeconds(out delay);
            int percent;
            start.IsEnabled = valid && displaySelection.Count > 0 && TryGetPercent(out percent);
            validation.Text = minutes.IsChecked == true ? "1~1,440분의 정수를 입력하세요." : "1~86,400초의 정수를 입력하세요.";
            validation.Visibility = valid ? W.Visibility.Collapsed : W.Visibility.Visible;
        }
        private bool StartWatching(bool quiet = false)
        {
            SetWatchIntent(true);
            return CommitProtectionChanges();
        }
        public void Pause(string message)
        {
            CancelAutomaticStart();
            protectionTimer.Stop();
            SetWatchIntent(false);
            reconnectTimer.Stop();
            displayRecovery.Stop();
            if (manualShade != null) manualShade.Controller.Clear();
            watcher.Stop(); status.Text = message;
            if (pauseItem != null) pauseItem.Text = UiText.Translate("감시 시작 / 다시 시작");
            if (tray != null) tray.Text = UiText.Translate("MonitorShade · 일시정지");
        }
        private void Diagnose()
        {
            bool resume = watchEnabled;
            Pause("진단 중"); diagnosing = true; SetLayoutHook();
            try
            {
                Hide();
                string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MonitorShade.diagnostics.txt");
                int code = Verification.Run(report);
                if (code == 0 && Preferences.Read(preferencesPath).SelectedDevices().Length > 0)
                {
                    // Exercise the same hidden constructor and timer used by --autostart.
                    using (SettingsForm probe = new SettingsForm(false, true))
                    {
                        System.Diagnostics.Stopwatch deadline = System.Diagnostics.Stopwatch.StartNew();
                        while (!probe.watcher.Running && deadline.ElapsedMilliseconds < 6000)
                        {
                            Application.DoEvents(); System.Threading.Thread.Sleep(10);
                        }
                        bool passed = probe.watcher.RunningCount == probe.savedStartupTargets.Length && probe.watcher.WaitingCount == 0 && !probe.Visible && !probe.startupPending;
                        File.AppendAllText(report, (passed ? "PASS" : "FAIL") + ": Hidden automatic startup starts saved monitor through startup timer" + Environment.NewLine);
                        if (!passed) code = 1;
                    }
                }
                status.Text = code == 0 ? "동작 진단 통과" : "진단 실패 · 결과 파일 확인";
                Find<C.TextBlock>("HelpStatus").Text = status.Text;
            }
            finally { diagnosing = false; SetLayoutHook(); if (resume) { SetWatchIntent(true); CommitProtectionChanges(); } Show(); Activate(); }
        }
        private void Toggle() { ChangeMonitoring(!watchEnabled && manualShade.Controller.Count == 0); }
        private void OpenSettings() { Show(); WindowState = W.WindowState.Normal; Activate(); }
        private void Dispatch(Action action) { if (!IsDisposed && IsHandleCreated) try { BeginInvoke(action); } catch (InvalidOperationException) { } }
        private void UpdateWatchingStatus()
        {
            int waiting = watcher.WaitingCount, active = watcher.RunningCount - waiting;
            status.Text = watcher.Running ? (waiting == 0 ? "감시 중" : active == 0 ? "화면 전환 대기 중" : active + "대 감시 · " + waiting + "대 대기") : "감시 중지";
            if (displayRecovery.Enabled && disconnectedCount > 0) status.Text = active + "대 감시 · " + disconnectedCount + "대 연결 대기";
            if (watchEnabled && displaySelection.Count == 0) status.Text = "모니터를 선택하세요.";
            if (!watchEnabled) status.Text = "자동 감시 꺼짐";
            tray.Text = UiText.Translate("MonitorShade · " + active + "대 감시" + (waiting > 0 ? " · " + waiting + "대 대기" : ""));
            if (displayRecovery.Enabled && disconnectedCount > 0) tray.Text = UiText.Translate("MonitorShade · " + status.Text);
            pauseItem.Text = UiText.Translate(watchEnabled ? "감시 일시정지" : "감시 시작 / 다시 시작");
            if (manualShade.Controller.Count > 0) { status.Text = manualShade.Controller.Count + "대 강제 가림 중"; tray.Text = UiText.Translate("MonitorShade · " + status.Text); }
        }
        private void DisplayChanged(object sender, EventArgs e)
        {
            Dispatch(delegate
            {
                arranger.Reset(); brightnessPage.Invalidate(); manualShade.Controller.Clear(); manualShade.RefreshTargets();
                ReconcileDisplays();
            });
        }
        private void ReconcileDisplays()
        {
            if (exiting || diagnosing || IsDisposed) return;
            try
            {
                RefreshDisplays();
                if (!displayRecovery.Enabled) return;
                watcher.Suspend(SystemSuspended);
                displayRecovery.Refresh(displaySelection.Resolve(connectedDisplays));
                if (!preview && savedSelectionRevision != displaySelection.Revision)
                {
                    Preferences saved = Preferences.Read(preferencesPath);
                    displaySelection.Store(saved); saved.Write(preferencesPath);
                    savedSelectionRevision = displaySelection.Revision;
                }
                UpdateWatchingStatus();
            }
            catch (Exception)
            {
                // Windows can expose an incomplete topology while a cable or docking station settles.
                // Preserve intent and retry; the per-monitor watcher handles unavailable capture safely.
                if (displayRecovery.Enabled && tray != null) status.Text = "모니터 연결 확인 중";
            }
        }
        private void RestartLayoutHook() { arranger.Reset(); if (layoutHook != null) { layoutHook.Dispose(); layoutHook = null; } SetLayoutHook(); }
        private void SessionChanged(object sender, SessionSwitchEventArgs e)
        {
            Dispatch(delegate
            {
                switch (e.Reason)
                {
                    case SessionSwitchReason.SessionLock: case SessionSwitchReason.SessionLogoff:
                    case SessionSwitchReason.RemoteDisconnect: case SessionSwitchReason.ConsoleDisconnect: sessionBlocked = true; break;
                    case SessionSwitchReason.SessionUnlock: case SessionSwitchReason.SessionLogon:
                    case SessionSwitchReason.RemoteConnect: case SessionSwitchReason.ConsoleConnect: sessionBlocked = false; break;
                    default: return;
                }
                ApplySystemSuspension();
            });
        }
        private void PowerChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend || e.Mode == PowerModes.Resume)
                Dispatch(delegate { powerBlocked = e.Mode == PowerModes.Suspend; ApplySystemSuspension(); });
        }
        private void ApplySystemSuspension()
        {
            if (SystemSuspended) manualShade.Controller.Clear();
            watcher.Suspend(SystemSuspended);
            brightnessPage.Suspend(SystemSuspended); brightnessPage.Invalidate();
            if (SystemSuspended) { arranger.Reset(); SetLayoutHook(); }
            else RestartLayoutHook();
            if (watcher.Running) UpdateWatchingStatus();
        }
        private void ThemeChanged(object sender, UserPreferenceChangedEventArgs e) { Dispatch(delegate { ApplyTheme(null); }); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == settingsMessage || (m.Msg == 0x0312 && m.WParam.ToInt32() == 2)) { m.Msg = 0; OpenSettings(); return; }
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1) { m.Msg = 0; Toggle(); return; }
            if (m.Msg == 0x0312 && (m.WParam.ToInt32() == 3 || m.WParam.ToInt32() == 4))
            { m.Msg = 0; if (!SystemSuspended && !diagnosing && manualShade != null) manualShade.Shortcut(m.WParam.ToInt32() - 3); return; }
            if (m.Msg == 0x031E && IsHandleCreated) ApplyTheme(null);
            base.WndProc(ref m);
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (protectionTimer.Enabled) CommitProtectionChanges(); if (!preview && !exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; } base.OnFormClosing(e); }
        private void ExitApplication() { exiting = true; Close(); }
        internal void AllowSystemExit() { exiting = true; }
        protected override void Dispose(bool disposing)
        {
            if (IsDisposed) return;
            exiting = true;
            if (disposing)
            {
                if (!preview)
                {
                    SystemEvents.DisplaySettingsChanged -= DisplayChanged; SystemEvents.SessionSwitch -= SessionChanged;
                    SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.UserPreferenceChanged -= ThemeChanged;
                }
                if (hotkey && IsHandleCreated) Native.UnregisterHotKey(Handle, 1);
                if (settingsHotkey && IsHandleCreated) Native.UnregisterHotKey(Handle, 2);
                if (manualShade != null) { manualShade.Controller.Changed -= UpdateWatchingStatus; manualShade.Dispose(); }
                watcher.Dispose();
                if (layoutHook != null) layoutHook.Dispose();
                if (brightnessPage != null) brightnessPage.Dispose();
                if (pageTransition != null) pageTransition.Dispose();
                if (backdrop != null) backdrop.Dispose();
                if (webUi != null) webUi.Dispose();
                arranger.Dispose();
                startupTimer.Dispose(); reconnectTimer.Dispose(); displayRecovery.Dispose(); appearanceTimer.Dispose(); protectionTimer.Dispose();
                if (tray != null) { tray.Visible = false; ContextMenuStrip menu = tray.ContextMenuStrip; tray.Dispose(); if (menu != null) menu.Dispose(); }
                if (trayIcon != null) trayIcon.Dispose();
            }
            base.Dispose(disposing);
        }

        internal void ExportPreview(string path, string mode)
        {
            if (mode == "--ui-test") { VerifyUi(path); return; }
            ApplyTheme(mode != "--preview-light");
            RenderPreview(path, 1020, 880);
        }
        private void RenderPreview(string path, int width, int height)
        {
            foreach (string name in new[] { "Protection", "Layout", "Brightness", "Shortcuts", "General", "Help" }) NavigationFeedback.Update(Find<C.RadioButton>("Nav" + name), false, false);
            Width = width + 16; Height = height + 40;
            ui.HorizontalAlignment = W.HorizontalAlignment.Left; ui.VerticalAlignment = W.VerticalAlignment.Top;
            ui.Width = width; ui.Height = height;
            ui.Measure(new W.Size(width, height)); ui.Arrange(new W.Rect(0, 0, width, height)); ui.UpdateLayout();
            DrawMap(); ui.UpdateLayout();
            ui.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(delegate { }));
            I.RenderTargetBitmap bitmap = new I.RenderTargetBitmap(width, height, 96, 96, M.PixelFormats.Pbgra32);
            bitmap.Render(ui);
            I.PngBitmapEncoder encoder = new I.PngBitmapEncoder(); encoder.Frames.Add(I.BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }
        private void VerifyUi(string path)
        {
            suppressMotion = true; pageTransition.Finish();
            List<string> results = new List<string>();
            Action<bool, string> check = delegate(bool valid, string name) { if (!valid) throw new Exception("FAIL: " + name); results.Add("PASS: " + name); };
            try
            {
                check(Text == "MonitorShade", "Title is MonitorShade only");
                check(StartupRegistration.Command(@"C:\Apps With Spaces\MonitorShade.exe") == "\"C:\\Apps With Spaces\\MonitorShade.exe\" --autostart", "Startup command quotes executable path and includes autostart flag");
                check(StartupRegistration.FindTarget("") == null && StartupRegistration.FindTarget("missing-display") == null, "Automatic startup never falls back to a different monitor");
                check(autoStart != null, "Login startup can be enabled and disabled in settings");
                manualShade.VerifyUi(check);
                check(ignoreWallpaper != null && ignoreWallpaper.IsChecked == Preferences.Read(preferencesPath).IgnoreWallpaper, "Wallpaper option loads saved preference");
                bool wallpaperChoice = ignoreWallpaper.IsChecked == true;
                ignoreWallpaper.IsChecked = !wallpaperChoice;
                check(ignoreWallpaper.IsChecked == !wallpaperChoice, "Wallpaper option can be toggled");
                ignoreWallpaper.IsChecked = wallpaperChoice;
                Pause("일시정지됨");
                SessionChanged(this, new SessionSwitchEventArgs(SessionSwitchReason.SessionLock)); Application.DoEvents();
                PowerChanged(this, new PowerModeChangedEventArgs(PowerModes.Suspend)); Application.DoEvents();
                SessionChanged(this, new SessionSwitchEventArgs(SessionSwitchReason.SessionUnlock)); Application.DoEvents();
                check(SystemSuspended && !watcher.Running, "Unlock cannot clear an outstanding power suspension or restart a manual pause");
                PowerChanged(this, new PowerModeChangedEventArgs(PowerModes.Resume)); Application.DoEvents();
                check(!SystemSuspended && !watcher.Running && status.Text == "일시정지됨", "System resume preserves manual pause and its status");
                SessionChanged(this, new SessionSwitchEventArgs(SessionSwitchReason.SessionLock)); Application.DoEvents();
                PowerChanged(this, new PowerModeChangedEventArgs(PowerModes.Resume)); Application.DoEvents();
                check(SystemSuspended, "Power resume cannot clear an outstanding session lock");
                SessionChanged(this, new SessionSwitchEventArgs(SessionSwitchReason.SessionUnlock)); Application.DoEvents();
                check(!SystemSuspended && !watcher.Running, "Unlock leaves a stopped watcher stopped");
                seconds.IsChecked = true; amount.Text = "5";
                int delay;
                check(TryGetSeconds(out delay) && delay == 5, "5 seconds maps to 5 seconds");
                minutes.IsChecked = true;
                check(TryGetSeconds(out delay) && delay == 300, "5 minutes maps to 300 seconds");
                seconds.IsChecked = true; amount.Text = "0";
                check(!TryGetSeconds(out delay) && !start.IsEnabled, "Zero duration cannot start");
                amount.Text = "abc"; check(!start.IsEnabled, "Invalid pasted text cannot start");
                amount.Text = "86400"; check(TryGetSeconds(out delay) && delay == 86400, "Maximum seconds accepted");
                minutes.IsChecked = true; check(!start.IsEnabled, "Minutes have their own maximum");
                seconds.IsChecked = true; amount.Text = "120";
                Find<C.RadioButton>("NavProtection").IsChecked = true;
                foreach (bool light in new[] { false, true })
                {
                    ApplyTheme(!light);
                    RenderPreview(Path.Combine(Path.GetDirectoryName(path), light ? "MonitorShade-light.png" : "MonitorShade-dark.png"), 1020, 880);
                    W.Point numeric = amount.TransformToAncestor(ui).Transform(new W.Point(0, 0));
                    W.Point unit = seconds.TransformToAncestor(ui).Transform(new W.Point(0, 0));
                    check(Math.Abs(numeric.Y - unit.Y) < 0.5 && Math.Abs(amount.ActualHeight - seconds.ActualHeight) < 0.5, (light ? "Light" : "Dark") + " numeric input and units align vertically");
                    W.Point selector = displays.TransformToAncestor(ui).Transform(new W.Point(0, 0));
                    check(Math.Abs(selector.X - numeric.X) < 0.5, "Display selector and time input share left alignment");
                    check(map.Children.Count == displays.Items.Count, "Map has one selectable button per monitor");
                }
                if (displays.Items.Count > 1)
                {
                    displays.SelectedItems.Clear(); displays.SelectedItems.Add(displays.Items[1]);
                    check(CurrentDevices().SequenceEqual(new[] { ((DisplayChoice)displays.Items[1]).Screen.DeviceName }), "Display selection can target one monitor");
                    C.Button tile = (C.Button)map.Children[0]; tile.RaiseEvent(new W.RoutedEventArgs(C.Button.ClickEvent));
                    check(CurrentDevices().Length == 2, "Clicking second monitor adds it without clearing first");
                    tile = (C.Button)map.Children[0]; tile.RaiseEvent(new W.RoutedEventArgs(C.Button.ClickEvent));
                    check(CurrentDevices().Length == 1, "Clicking selected monitor removes only that monitor");
                    displays.SelectedItems.Clear(); check(!start.IsEnabled, "No selected monitors cannot start");
                    foreach (object item in displays.Items) displays.SelectedItems.Add(item);
                }
                Find<C.RadioButton>("NavLayout").IsChecked = true;
                check(Find<C.ScrollViewer>("LayoutPage").Visibility == W.Visibility.Visible && Find<C.ScrollViewer>("ProtectionPage").Visibility == W.Visibility.Collapsed, "Sidebar switches to layout without another window");
                centerPercent.Text = "65"; int percent;
                check(TryGetPercent(out percent) && percent == 65, "Center percentage accepts 65");
                foreach (string invalid in new[] { "9", "101", "abc", "" }) { centerPercent.Text = invalid; check(!Find<C.Button>("ApplyCenter").IsEnabled, "Reject invalid center percentage: " + invalid); }
                centerPercent.Text = "65";
                foreach (bool light in new[] { false, true }) { ApplyTheme(!light); RenderPreview(Path.Combine(Path.GetDirectoryName(path), light ? "Layout-light.png" : "Layout-dark.png"), 1020, 880); }
                Find<C.RadioButton>("NavGeneral").IsChecked = true;
                check(Find<C.ScrollViewer>("GeneralPage").Visibility == W.Visibility.Visible, "Sidebar switches to general settings");
                RenderPreview(Path.Combine(Path.GetDirectoryName(path), "General.png"), 1020, 880);
                Find<C.RadioButton>("NavHelp").IsChecked = true;
                check(Find<C.ScrollViewer>("HelpPage").Visibility == W.Visibility.Visible && Find<C.ScrollViewer>("GeneralPage").Visibility == W.Visibility.Collapsed, "Sidebar opens help within the settings window");
                foreach (bool light in new[] { false, true }) { ApplyTheme(!light); RenderPreview(Path.Combine(Path.GetDirectoryName(path), light ? "Help-light.png" : "Help-dark.png"), 1020, 880); }
                Find<C.Expander>("HelpProtection").IsExpanded = false;
                Find<C.Expander>("HelpBrightness").IsExpanded = true;
                RenderPreview(Path.Combine(Path.GetDirectoryName(path), "Help-brightness.png"), 1020, 880);
                Find<C.RadioButton>("NavBrightness").IsChecked = true;
                check(Find<C.ScrollViewer>("BrightnessPage").Visibility == W.Visibility.Visible, "Sidebar switches to monitor brightness");
                brightnessPage.VerifyUi(check);
                foreach (bool light in new[] { false, true })
                {
                    ApplyTheme(!light); Find<C.ScrollViewer>("BrightnessPage").ScrollToTop();
                    Application.DoEvents(); ui.UpdateLayout();
                    RenderPreview(Path.Combine(Path.GetDirectoryName(path), light ? "Brightness-light.png" : "Brightness-dark.png"), 1020, 880);
                    var number = Find<AnimatedPercent>("MasterPercent");
                    check(!string.IsNullOrWhiteSpace(number.Text) && number.ActualWidth > 20 && number.ActualHeight > 30, "Master readout has visible content and a measured layout");
                }
                Find<C.RadioButton>("NavShortcuts").IsChecked = true;
                check(Find<C.ScrollViewer>("ShortcutsPage").Visibility == W.Visibility.Visible, "Brightness shortcuts have their own sidebar page");
                Find<C.ComboBox>("BrightnessUp").SelectedIndex = Find<C.ComboBox>("BrightnessDown").SelectedIndex;
                brightnessPage.ApplySettings();
                check(Find<C.TextBlock>("ShortcutsStatus").Visibility == W.Visibility.Visible && Find<C.TextBlock>("ShortcutsStatus").Text.Length > 0, "Shortcut validation appears on the shortcuts page");
                RenderPreview(Path.Combine(Path.GetDirectoryName(path), "Brightness-shortcuts.png"), 1020, 880);
                Find<C.RadioButton>("NavProtection").IsChecked = true;
                RenderPreview(Path.Combine(Path.GetDirectoryName(path), "MonitorShade-compact.png"), 960, 640);
                check(start.ActualWidth >= 112 && seconds.ActualWidth == 56 && minutes.ActualWidth == 56, "Compact layout keeps actions and both units usable");
                VerifyPresentation(check);
                results.Add("UI rendering and interaction checks completed.");
                File.WriteAllLines(path, results.ToArray());
            }
            catch (Exception e) { results.Add(e.ToString()); File.WriteAllLines(path, results.ToArray()); Environment.ExitCode = 1; throw; }
        }
        private static void PumpFrames(int milliseconds)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
            timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        private static IEnumerable<C.TextBlock> TextDescendants(W.DependencyObject root)
        {
            var text = root as C.TextBlock; if (text != null) yield return text;
            for (int i = 0; i < M.VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in TextDescendants(M.VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        internal int VerifyLiveText(string path, bool pretendard)
        {
            var results = new List<string>(); bool failed = false;
            appearanceTimer.Stop();
            Find<C.ComboBox>("KoreanFont").SelectedIndex = pretendard ? 1 : 0;
            results.Add("INFO: Font=" + appearance.KoreanFont);
            Find<C.ComboBox>("AppearanceMode").SelectedIndex = 1;
            suppressMotion = false;
            foreach (string mode in new[] { "solid-no-fade", "solid", "glass", "glass-rapid", "solid-again", "interrupted" })
            {
                suppressMotion = mode == "solid-no-fade";
                Find<C.ComboBox>("AppearanceMode").SelectedIndex = mode.StartsWith("glass") ? 3 : 1;
                Find<C.CheckBox>("UseBackdrop").IsChecked = mode.StartsWith("glass");
                Find<C.RadioButton>("NavProtection").IsChecked = true; PumpFrames(450);
                if (mode == "glass-rapid")
                {
                    Find<C.RadioButton>("NavHelp").IsChecked = true; PumpFrames(30);
                    Find<C.RadioButton>("NavLayout").IsChecked = true; PumpFrames(30);
                }
                if (mode == "interrupted")
                {
                    Find<C.RadioButton>("NavGeneral").IsChecked = true; PumpFrames(30);
                    Hide(); Show(); Activate();
                }
                Find<C.RadioButton>("NavGeneral").IsChecked = true; PumpFrames(1000);
                results.Add("INFO: " + mode + ", composed=" + backdrop.Active + ", error=" + backdrop.Error + ", DPI=" + W.PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11 * 96);
                if (mode.StartsWith("glass") && !backdrop.Active)
                { results.Add("FAIL: Composed glass could not be tested: " + backdrop.Error); failed = true; continue; }
                foreach (string label in new[] { "시간별 색상", "한글 글꼴", "창 뒤 배경 블러", pretendard ? "Pretendard" : "맑은 고딕 · 기본" })
                {
                    var text = TextDescendants(ui).First(t => t.IsVisible && t.Text == label);
                    var local = text.ReadLocalValue(M.TextOptions.TextRenderingModeProperty);
                    var effective = M.TextOptions.GetTextRenderingMode(text);
                    using (var normal = TextRasterVerification.CapturePixels(text, this))
                    {
                        M.TextOptions.SetTextRenderingMode(text, M.TextRenderingMode.Grayscale); PumpFrames(150);
                        using (var gray = TextRasterVerification.CapturePixels(text, this))
                        {
                            int edges = 0;
                            int ink = 0;
                            for (int y = 0; y < normal.Height; y++) for (int x = 0; x < normal.Width; x++)
                            {
                                var a = normal.GetPixel(x, y); var b = gray.GetPixel(x, y);
                                int r = a.R - b.R, g = a.G - b.G, blue = a.B - b.B;
                                if (Math.Max(r, Math.Max(g, blue)) - Math.Min(r, Math.Min(g, blue)) > 12) edges++;
                                if (Math.Abs(a.R - normal.GetPixel(normal.Width - 1, 0).R) > 70) ink++;
                            }
                            bool pass = edges > 30 && ink > 30; failed |= !pass;
                            results.Add((pass ? "PASS: " : "FAIL: ") + mode + " / " + label + ": mode=" + effective + ", subpixel differences=" + edges);
                            normal.Save(path + "." + mode + "." + label + ".png", System.Drawing.Imaging.ImageFormat.Png);
                            gray.Save(path + "." + mode + "." + label + ".gray.png", System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    if (local == W.DependencyProperty.UnsetValue)
                    {
                        M.TextOptions.SetTextRenderingMode(text, M.TextRenderingMode.Auto);
                        text.ClearValue(M.TextOptions.TextRenderingModeProperty);
                    }
                    else text.SetValue(M.TextOptions.TextRenderingModeProperty, local);
                    PumpFrames(150);
                }
            }
            File.WriteAllLines(path, results); return failed ? 1 : 0;
        }
        private void VerifyPresentation(Action<bool, string> check)
        {
            var chrome = W.Shell.WindowChrome.GetWindowChrome(this);
            check(WindowStyle == W.WindowStyle.None && !AllowsTransparency && chrome.CaptionHeight == 40 && chrome.ResizeBorderThickness.Left >= 5 && !chrome.UseAeroCaptionButtons, "Custom caption replaces the system bar while preserving native resize and drag regions");
            foreach (string button in new[] { "CaptionMinimize", "CaptionMaximize", "CaptionClose" })
                check(W.Shell.WindowChrome.GetIsHitTestVisibleInChrome(Find<C.Button>(button)), button + " remains clickable inside the caption");
            M.GlyphTypeface regular, semibold;
            string expectedFamily = appearance.KoreanFont == "MalgunGothic" && TextPresentation.HasWindowsFont ? "Malgun Gothic" : "Pretendard";
            check(new M.Typeface(ui.FontFamily, W.FontStyles.Normal, W.FontWeights.Normal, W.FontStretches.Normal).TryGetGlyphTypeface(out regular) && regular.FamilyNames.Values.Contains(expectedFamily) && regular.CharacterToGlyphMap.ContainsKey('한'), "Selected Korean font resolves its actual regular glyphs: " + expectedFamily);
            check(new M.Typeface(ui.FontFamily, W.FontStyles.Normal, W.FontWeights.SemiBold, W.FontStretches.Normal).TryGetGlyphTypeface(out semibold) && semibold.FamilyNames.Values.Contains(expectedFamily) && semibold.StyleSimulations == M.StyleSimulations.None, "Selected Korean font resolves an actual heading face without synthesized weight");
            check(TextPresentation.KoreanFamily("MalgunGothic", false) == TextPresentation.DesignFont, "A missing Windows Korean font falls back to bundled Pretendard");
            bool originalBlur = appearance.Blur; string originalTheme = appearance.Theme, originalFont = appearance.KoreanFont;
            var originalNumbers = ui.Resources["NumberFont"].ToString();
            Find<C.ComboBox>("KoreanFont").SelectedIndex = 1;
            check(appearance.KoreanFont == "Pretendard" && ui.FontFamily == TextPresentation.DesignFont, "Selecting Pretendard immediately updates the UI font");
            Find<C.ComboBox>("KoreanFont").SelectedIndex = 0;
            check(appearance.KoreanFont == "MalgunGothic" && ui.FontFamily == TextPresentation.KoreanFamily("MalgunGothic", TextPresentation.HasWindowsFont), "Selecting Malgun Gothic immediately updates the UI font");
            check(originalBlur == appearance.Blur && originalTheme == appearance.Theme && originalNumbers == ui.Resources["NumberFont"].ToString(), "Switching Korean fonts preserves blur, theme and Manrope numbers");
            Find<C.ComboBox>("KoreanFont").SelectedIndex = originalFont == "Pretendard" ? 1 : 0;
            check(ui.UseLayoutRounding && ui.SnapsToDevicePixels && M.TextOptions.GetTextFormattingMode(ui) == M.TextFormattingMode.Display && M.TextOptions.GetTextHintingMode(ui) == M.TextHintingMode.Fixed, "Text uses pixel-aligned display metrics and fixed hinting");
            var surface = new C.Grid(); TextPresentation.Apply(surface, true);
            check(M.TextOptions.GetTextRenderingMode(surface) == M.TextRenderingMode.Grayscale && M.RenderOptions.GetClearTypeHint(surface) == M.ClearTypeHint.Auto, "Native glass fallback avoids invalid subpixel blending");
            TextPresentation.Apply(surface, true, true);
            check(M.TextOptions.GetTextRenderingMode(surface) == M.TextRenderingMode.ClearType && M.RenderOptions.GetClearTypeHint(surface) == M.ClearTypeHint.Enabled, "Composed RGB background enables ClearType without opaque cards");
            TextPresentation.Apply(surface, false);
            check(M.TextOptions.GetTextRenderingMode(surface) == M.TextRenderingMode.ClearType && M.RenderOptions.GetClearTypeHint(surface) == M.ClearTypeHint.Enabled, "Opaque mode enables ClearType");
            check(((M.SolidColorBrush)ui.Resources["Card"]).Color.A == (appearance.Blur && DesktopBackdrop.TransparencyEnabled ? 210 : 255), "Cards retain 1.12 alpha instead of an opaque text surface");
            var feedback = Find<C.TextBlock>("BrightnessStatus");
            check(feedback.Parent is C.StackPanel, "Feedback stays directly on the glass canvas with no opaque patch");
            PumpFrames(500); // Let resized backdrop/theme callbacks settle before synthetic hover.

            var nav = Find<C.RadioButton>("NavGeneral"); nav.ApplyTemplate();
            var navSurface = (C.Border)nav.Template.FindName("Surface", nav);
            var indicator = (C.Border)nav.Template.FindName("Indicator", nav);
            NavigationFeedback.Update(nav, true, true); PumpFrames(240);
            check(navSurface.Opacity >= .59 && nav.IsChecked != true && Find<C.ScrollViewer>("ProtectionPage").Visibility == W.Visibility.Visible, "Navigation hover fades its surface without selecting a page");
            NavigationFeedback.Update(nav, false, true); PumpFrames(240);
            check(navSurface.Opacity < .01 && indicator.Opacity < .01, "Navigation hover and indicator settle correctly on pointer leave");
            suppressMotion = false;
            var first = Find<C.ScrollViewer>("ProtectionPage"); var last = Find<C.ScrollViewer>("GeneralPage");
            string draft = amount.Text;
            SelectPage("General");
            bool animation = W.SystemParameters.ClientAreaAnimation && !W.SystemParameters.HighContrast;
            check(!animation || (!Find<C.Grid>("PageHost").IsHitTestVisible && first.Visibility == W.Visibility.Visible), "Page fade starts on the outgoing page and suppresses stale clicks");
            PumpFrames(650);
            check(last.Visibility == W.Visibility.Visible && first.Visibility == W.Visibility.Collapsed && last.Opacity == 1 && Find<C.Grid>("PageHost").IsHitTestVisible, "Page fade finishes with the new page fully opaque and interactive: " + last.Visibility + ", old=" + first.Visibility + ", opacity=" + last.Opacity + ", hit=" + Find<C.Grid>("PageHost").IsHitTestVisible);
            SelectPage("Help"); PumpFrames(30); SelectPage("Layout"); PumpFrames(30); SelectPage("Brightness"); PumpFrames(400);
            check(Find<C.ScrollViewer>("BrightnessPage").Visibility == W.Visibility.Visible && new[] { "Protection", "Layout", "Brightness", "Shortcuts", "General", "Help" }.Count(n => Find<C.ScrollViewer>(n + "Page").Visibility == W.Visibility.Visible) == 1, "Rapid navigation keeps only the last requested page visible");
            SelectPage("Protection"); PumpFrames(650);
            check(amount.Text == draft && first.Opacity == 1, "Navigation preserves unsaved form values and restores full text opacity");
            SelectPage("Help"); pageTransition.Finish(); PumpFrames(350);
            check(Find<C.ScrollViewer>("HelpPage").Visibility == W.Visibility.Visible && Find<C.Grid>("PageHost").IsHitTestVisible, "Canceling a transition prevents stale callbacks and restores interaction");
            suppressMotion = true;
            Find<C.Button>("CaptionMaximize").RaiseEvent(new W.RoutedEventArgs(C.Button.ClickEvent)); PumpFrames(180);
            check(WindowState == W.WindowState.Maximized && Find<C.TextBlock>("CaptionMaximizeGlyph").Text == "\uE923", "Custom maximize button updates window state and restore glyph");
            Find<C.Button>("CaptionMaximize").RaiseEvent(new W.RoutedEventArgs(C.Button.ClickEvent)); PumpFrames(180);
            check(WindowState == W.WindowState.Normal, "Custom restore button restores normal window state");
            Find<C.Button>("CaptionMinimize").RaiseEvent(new W.RoutedEventArgs(C.Button.ClickEvent)); PumpFrames(180);
            check(WindowState == W.WindowState.Minimized, "Custom minimize button minimizes the native window");
            W.SystemCommands.RestoreWindow(this); PumpFrames(120);
            Find<C.Button>("CaptionClose").RaiseEvent(new W.RoutedEventArgs(C.Button.ClickEvent)); PumpFrames(120);
            check(IsDisposed, "Custom close button follows the window lifetime and disposes the preview cleanly");
        }
    }
}
