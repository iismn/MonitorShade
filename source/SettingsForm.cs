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

    internal sealed class SettingsForm : Form
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
        private readonly Timer startupTimer = new Timer { Interval = 1000 };
        private bool startupPending, updatingStartup;
        private string[] savedStartupTargets;
        private int startupAttempts;
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem pauseItem;
        private bool exiting, hotkey, settingsHotkey, diagnosing, dark;
        private bool allowSettingsWindow;
        private bool sessionBlocked, powerBlocked;
        private bool SystemSuspended { get { return sessionBlocked || powerBlocked; } }
        private readonly uint settingsMessage = Native.RegisterWindowMessage("MonitorShade.ShowSettings.v1");

        public SettingsForm(bool previewMode, bool automatic = true)
        {
            SuspendLayout();
            preview = previewMode;
            displayRecovery = new DisplayRecovery(watcher);
            reconnectTimer.Tick += delegate { ReconcileDisplays(); };
            startupPending = automatic && !preview;
            allowSettingsWindow = !startupPending;
            Text = "MonitorShade";
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1020, 880);
            MinimumSize = new Size(980, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
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
            Controls.Add(new ElementHost { Child = ui, Dock = DockStyle.Fill });
            ApplyTheme(null);

            Preferences preferences = File.Exists(preferencesPath) ? Preferences.Read(preferencesPath) : new Preferences { WaitValue = 120, Minutes = false };
            if (!File.Exists(preferencesPath)) preferences.DeviceNames = Screen.AllScreens.Select(s => s.DeviceName).ToArray();
            displaySelection = new DisplaySelection(preferences);
            brightnessPage = new BrightnessPage(ui, preview ? new BrightnessPreferences() : preferences.Brightness, preferencesPath, preview);
            manualShade = new ManualShadePage(ui, preview ? new ManualShadePreferences() : preferences.ManualShade, preferencesPath, preview, delegate { return Handle; }, watcher);
            brightnessPage.BindingChanged += delegate { SetLayoutHook(); };
            appliedLayoutEnabled = preferences.LayoutEnabled; appliedCenterPercent = preferences.CenterPercent;
            layoutEnabled.IsChecked = appliedLayoutEnabled; centerPercent.Text = appliedCenterPercent.ToString(CultureInfo.InvariantCulture);
            centerPercent.TextChanged += delegate { ValidateLayout(); };
            centerPercent.PreviewTextInput += delegate(object sender, System.Windows.Input.TextCompositionEventArgs e) { e.Handled = e.Text.Any(c => c < '0' || c > '9'); };
            Find<C.RadioButton>("NavProtection").Checked += delegate { SelectPage("Protection"); };
            Find<C.RadioButton>("NavLayout").Checked += delegate { SelectPage("Layout"); };
            Find<C.RadioButton>("NavGeneral").Checked += delegate { SelectPage("General"); };
            Find<C.RadioButton>("NavHelp").Checked += delegate { SelectPage("Help"); };
            Find<C.RadioButton>("NavBrightness").Checked += async delegate { SelectPage("Brightness"); await brightnessPage.Refresh(false); };
            Find<C.Button>("ApplyLayout").Click += delegate { ApplyLayoutSettings(); };
            Find<C.Button>("TestLayout").Click += delegate { DiagnoseLayout(); };
            savedStartupTargets = preferences.SelectedDevices();
            if (!File.Exists(preferencesPath)) savedStartupTargets = Screen.AllScreens.Select(s => s.DeviceName).ToArray();
            amount.Text = preferences.WaitValue.ToString(CultureInfo.InvariantCulture);
            minutes.IsChecked = preferences.Minutes; seconds.IsChecked = !preferences.Minutes;
            ignoreWallpaper.IsChecked = preferences.IgnoreWallpaper;
            displays.SelectionChanged += delegate
            {
                if (!refreshingDisplays) displaySelection.Edit(connectedDisplays, CurrentDevices());
                UpdateDisplay();
            };
            map.SizeChanged += delegate { DrawMap(); };
            RefreshDisplays();
            amount.TextChanged += delegate { ValidateWait(); };
            seconds.Checked += delegate { ValidateWait(); };
            minutes.Checked += delegate { ValidateWait(); };
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
            tray = new NotifyIcon { Icon = Icon, Text = "MonitorShade · 준비됨", ContextMenuStrip = menu, Visible = !preview };
            tray.DoubleClick += delegate { OpenSettings(); };
            arranger.Failed += delegate(string reason) { Find<C.TextBlock>("LayoutStatus").Text = reason; tray.ShowBalloonTip(3000, "MonitorShade 창 배치", reason, ToolTipIcon.Info); };
            watcher.StateChanged += UpdateWatchingStatus;
            manualShade.Controller.Changed += UpdateWatchingStatus;
            watcher.Failed += delegate(string reason) { UpdateWatchingStatus(); status.Text = watcher.RunningCount + "대 감시 · 일부 중지"; tray.ShowBalloonTip(4000, "MonitorShade 일부 감시 중지", reason, ToolTipIcon.Warning); };
            if (!preview)
            {
                SystemEvents.DisplaySettingsChanged += DisplayChanged;
                SystemEvents.SessionSwitch += SessionChanged;
                SystemEvents.PowerModeChanged += PowerChanged;
                SystemEvents.UserPreferenceChanged += ThemeChanged;
            }
            ResumeLayout(false); PerformLayout();
            ValidateLayout();
            if (!preview) { IntPtr messageWindow = Handle; SetLayoutHook(); manualShade.Register(); }
            if (!preview) BeginInvoke(new Action(async delegate { await brightnessPage.Refresh(false); }));
            if (startupPending)
            {
                IntPtr window = Handle; // Hidden message window for tray, hotkey and startup retry timer.
                status.Text = "자동 감시 준비 중";
                startupTimer.Tick += delegate { TryAutomaticStart(); };
                startupTimer.Start();
            }
        }

        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(value && allowSettingsWindow); }
        private void CancelAutomaticStart() { startupPending = false; startupTimer.Stop(); }
        private void TryAutomaticStart()
        {
            if (!startupPending) return;
            startupAttempts++;
            if (startupAttempts < 3) return;
            try
            {
                if (displaySelection.Count == 0) throw new InvalidOperationException("저장된 감시 설정을 확인해 주세요.");
                RefreshDisplays();
                if (!StartWatching(true)) throw new InvalidOperationException("대기 시간 설정을 확인해 주세요.");
                CancelAutomaticStart();
            }
            catch (Exception e)
            {
                if (startupAttempts < 30) return;
                CancelAutomaticStart(); Pause(e.Message);
                tray.ShowBalloonTip(4000, "MonitorShade 자동 시작 확인", e.Message, ToolTipIcon.Warning);
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
                status.Text = enable ? "로그인 시 자동 실행 및 감시 켜짐" : "자동 실행 꺼짐";
            }
            catch (Exception e)
            {
                updatingStartup = true;
                try { autoStart.IsChecked = StartupRegistration.Enabled; } catch { autoStart.IsChecked = false; }
                updatingStartup = false;
                MessageBox.Show(this, e.Message, "자동 실행 설정", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        private void SavePreferences()
        {
            Preferences saved = Preferences.Read(preferencesPath);
            displaySelection.Store(saved);
            saved.WaitValue = int.Parse(amount.Text, CultureInfo.InvariantCulture); saved.Minutes = minutes.IsChecked == true; saved.IgnoreWallpaper = ignoreWallpaper.IsChecked == true; saved.LayoutEnabled = appliedLayoutEnabled; saved.CenterPercent = appliedCenterPercent; saved.Write(preferencesPath);
            savedSelectionRevision = displaySelection.Revision;
        }

        private void SelectPage(string page)
        {
            foreach (string name in new[] { "Protection", "Layout", "Brightness", "General", "Help" }) Find<C.ScrollViewer>(name + "Page").Visibility = name == page ? W.Visibility.Visible : W.Visibility.Collapsed;
        }
        private bool TryGetPercent(out int value) { return int.TryParse(centerPercent.Text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 10 && value <= 100; }
        private void ValidateLayout()
        {
            int value; bool valid = TryGetPercent(out value);
            Find<C.TextBlock>("CenterValidation").Visibility = valid ? W.Visibility.Collapsed : W.Visibility.Visible;
            Find<C.Button>("ApplyLayout").IsEnabled = valid;
            if (amount != null && seconds != null && start != null) ValidateWait();
        }
        private void SetLayoutHook()
        {
            if (preview) return;
            try
            {
                if (!SystemSuspended && (appliedLayoutEnabled || brightnessPage.Binding.Enabled) && layoutHook == null)
                    layoutHook = new LayoutKeyboardHook(delegate(IntPtr window, LayoutDirection direction) { Dispatch(delegate { if (appliedLayoutEnabled && !diagnosing && !SystemSuspended && LayoutNative.GetForegroundWindow() == window) arranger.Apply(window, direction, appliedCenterPercent); }); }, delegate(int direction) { Dispatch(delegate { if (!diagnosing && !SystemSuspended) brightnessPage.Shortcut(direction); }); });
                if (layoutHook != null) layoutHook.Enabled = appliedLayoutEnabled && !diagnosing && !SystemSuspended;
                if (layoutHook != null) layoutHook.ConfigureBrightness(diagnosing || SystemSuspended ? null : brightnessPage.Binding);
                if (!appliedLayoutEnabled) arranger.Reset();
                Find<C.TextBlock>("LayoutStatus").Text = "";
            }
            catch (Exception e) { Find<C.TextBlock>("LayoutStatus").Text = "단축키를 시작할 수 없습니다: " + e.Message; }
        }
        private bool ApplyLayoutSettings()
        {
            int value; if (!TryGetPercent(out value)) { ValidateLayout(); return false; }
            try
            {
                if (!preview)
                {
                    Preferences saved = Preferences.Read(preferencesPath);
                    saved.LayoutEnabled = layoutEnabled.IsChecked == true; saved.CenterPercent = value; saved.Write(preferencesPath);
                }
                appliedLayoutEnabled = layoutEnabled.IsChecked == true; appliedCenterPercent = value;
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
        private void ApplyTheme(bool? forceDark)
        {
            dark = true;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))
                    if (key != null) dark = Convert.ToInt32(key.GetValue("AppsUseLightTheme", 0)) == 0;
            }
            catch { }
            if (forceDark.HasValue) dark = forceDark.Value;
            string[] names = { "Page", "Card", "Map", "Input", "Stroke", "Text", "Muted", "Accent", "AccentText", "Hover", "Monitor", "Selection" };
            string[] colors = dark ?
                new[] { "#202020", "#2B2B2B", "#1C1C1C", "#343434", "#494949", "#F3F3F3", "#C2C2C2", "#60CDFF", "#101010", "#404040", "#373737", "#0078D4" } :
                new[] { "#F3F3F3", "#FBFBFB", "#EEEEEE", "#FFFFFF", "#D6D6D6", "#1A1A1A", "#606060", "#005FB8", "#FFFFFF", "#E5E5E5", "#DADADA", "#005FB8" };
            for (int i = 0; i < names.Length; i++) ui.Resources[names[i]] = new M.SolidColorBrush((M.Color)M.ColorConverter.ConvertFromString(colors[i]));
            BackColor = ColorTranslator.FromHtml(colors[0]);
            if (IsHandleCreated) ApplyWindowStyle();
            if (map != null) DrawMap();
        }
        private void ApplyWindowStyle()
        {
            if (Environment.OSVersion.Version.Build < 22000) return;
            int darkValue = dark ? 1 : 0, rounded = 2;
            DwmSetWindowAttribute(Handle, 20, ref darkValue, 4);
            DwmSetWindowAttribute(Handle, 33, ref rounded, 4);
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
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
                    Background = Brush(selected ? "Selection" : "Monitor"), Foreground = selected ? M.Brushes.White : Brush("Text"),
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
            if (diagnosing) return false;
            int delay;
            if (!TryGetSeconds(out delay)) { ValidateWait(); if (!quiet) amount.Focus(); return false; }
            if (displaySelection.Count == 0) return false;
            try
            {
                RefreshDisplays();
                SavePreferences();
                if (!quiet && !ApplyLayoutSettings()) return false;
                if (!quiet && !brightnessPage.ApplySettings()) return false;
                if (!quiet && !manualShade.Apply()) return false;
                displayRecovery.Start(delay, ignoreWallpaper.IsChecked == true);
                ReconcileDisplays();
                reconnectTimer.Start();
                UpdateWatchingStatus();
                Hide(); if (!quiet) tray.ShowBalloonTip(2500, "MonitorShade 감시 중", "트레이 아이콘을 두 번 클릭하면 설정을 엽니다.", ToolTipIcon.Info);
                return true;
            }
            catch (Exception e) { if (quiet) throw; Pause(e.Message); MessageBox.Show(this, e.Message, "시작할 수 없습니다", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
        }
        public void Pause(string message)
        {
            CancelAutomaticStart();
            reconnectTimer.Stop();
            displayRecovery.Stop();
            if (manualShade != null) manualShade.Controller.Clear();
            watcher.Stop(); status.Text = message;
            if (pauseItem != null) pauseItem.Text = "감시 시작 / 다시 시작";
            if (tray != null) tray.Text = "MonitorShade · 일시정지";
        }
        private void Diagnose()
        {
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
            finally { diagnosing = false; SetLayoutHook(); Show(); Activate(); }
        }
        private void Toggle() { if (displayRecovery.Enabled || startupPending || manualShade.Controller.Count > 0) Pause("일시정지됨"); else StartWatching(); }
        private void OpenSettings() { Pause("일시정지됨"); allowSettingsWindow = true; Show(); WindowState = FormWindowState.Normal; Activate(); }
        private void Dispatch(Action action) { if (!IsDisposed && IsHandleCreated) try { BeginInvoke(action); } catch (InvalidOperationException) { } }
        private void UpdateWatchingStatus()
        {
            int waiting = watcher.WaitingCount, active = watcher.RunningCount - waiting;
            status.Text = watcher.Running ? (waiting == 0 ? "감시 중" : active == 0 ? "화면 전환 대기 중" : active + "대 감시 · " + waiting + "대 대기") : "감시 중지";
            if (displayRecovery.Enabled && disconnectedCount > 0) status.Text = active + "대 감시 · " + disconnectedCount + "대 연결 대기";
            tray.Text = "MonitorShade · " + active + "대 감시" + (waiting > 0 ? " · " + waiting + "대 대기" : "");
            if (displayRecovery.Enabled && disconnectedCount > 0) tray.Text = "MonitorShade · " + status.Text;
            pauseItem.Text = displayRecovery.Enabled ? "감시 일시정지" : "감시 시작 / 다시 시작";
            if (manualShade.Controller.Count > 0) { status.Text = manualShade.Controller.Count + "대 강제 가림 중"; tray.Text = "MonitorShade · " + status.Text; }
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
            if (m.Msg == settingsMessage || (m.Msg == 0x0312 && m.WParam.ToInt32() == 2)) { OpenSettings(); return; }
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1) { Toggle(); return; }
            if (m.Msg == 0x0312 && (m.WParam.ToInt32() == 3 || m.WParam.ToInt32() == 4))
            { if (!SystemSuspended && !diagnosing && manualShade != null) manualShade.Shortcut(m.WParam.ToInt32() - 3); return; }
            base.WndProc(ref m);
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (!preview && !exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; } base.OnFormClosing(e); }
        private void ExitApplication() { exiting = true; Close(); }
        protected override void Dispose(bool disposing)
        {
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
                arranger.Dispose();
                startupTimer.Dispose(); reconnectTimer.Dispose(); displayRecovery.Dispose();
                if (tray != null) { tray.Visible = false; ContextMenuStrip menu = tray.ContextMenuStrip; tray.Dispose(); if (menu != null) menu.Dispose(); }
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
            ui.HorizontalAlignment = W.HorizontalAlignment.Left; ui.VerticalAlignment = W.VerticalAlignment.Top;
            ui.Width = width; ui.Height = height;
            ui.Measure(new W.Size(width, height)); ui.Arrange(new W.Rect(0, 0, width, height)); ui.UpdateLayout();
            DrawMap(); ui.UpdateLayout();
            I.RenderTargetBitmap bitmap = new I.RenderTargetBitmap(width, height, 96, 96, M.PixelFormats.Pbgra32);
            bitmap.Render(ui);
            I.PngBitmapEncoder encoder = new I.PngBitmapEncoder(); encoder.Frames.Add(I.BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }
        private void VerifyUi(string path)
        {
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
                foreach (string invalid in new[] { "9", "101", "abc", "" }) { centerPercent.Text = invalid; check(!Find<C.Button>("ApplyLayout").IsEnabled, "Reject invalid center percentage: " + invalid); }
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
                foreach (bool light in new[] { false, true }) { ApplyTheme(!light); Find<C.ScrollViewer>("BrightnessPage").ScrollToTop(); RenderPreview(Path.Combine(Path.GetDirectoryName(path), light ? "Brightness-light.png" : "Brightness-dark.png"), 1020, 880); }
                Find<C.ScrollViewer>("BrightnessPage").ScrollToBottom(); RenderPreview(Path.Combine(Path.GetDirectoryName(path), "Brightness-shortcuts.png"), 1020, 880);
                Find<C.RadioButton>("NavProtection").IsChecked = true;
                RenderPreview(Path.Combine(Path.GetDirectoryName(path), "MonitorShade-compact.png"), 960, 640);
                check(start.ActualWidth >= 112 && seconds.ActualWidth == 56 && minutes.ActualWidth == 56, "Compact layout keeps actions and both units usable");
                results.Add("UI rendering and interaction checks completed.");
                File.WriteAllLines(path, results.ToArray());
            }
            catch (Exception e) { results.Add(e.ToString()); File.WriteAllLines(path, results.ToArray()); Environment.ExitCode = 1; throw; }
        }
    }
}
