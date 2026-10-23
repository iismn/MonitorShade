using System;
using C = System.Windows.Controls;
using System.Windows.Forms;

namespace MonitorShade
{
    internal sealed partial class SettingsForm
    {
        private bool watchEnabled, protectionReady, updatingWatchIntent;
        private readonly Timer protectionTimer = new Timer { Interval = 400 };

        private void InitializeProtection()
        {
            protectionTimer.Tick += delegate { CommitProtectionChanges(); };
            var toggle = Find<C.CheckBox>("WatchEnabled");
            toggle.Checked += delegate { if (!updatingWatchIntent) ChangeMonitoring(true); };
            toggle.Unchecked += delegate { if (!updatingWatchIntent) ChangeMonitoring(false); };
            protectionReady = true;
            if (!watchEnabled) status.Text = "자동 감시 꺼짐";
        }

        private void SetWatchIntent(bool enabled)
        {
            watchEnabled = enabled;
            updatingWatchIntent = true;
            try { Find<C.CheckBox>("WatchEnabled").IsChecked = enabled; }
            finally { updatingWatchIntent = false; }
        }

        private void ChangeMonitoring(bool enabled)
        {
            CancelAutomaticStart();
            SetWatchIntent(enabled);
            // Turning protection off must work even with an unfinished timeout.
            if (!enabled) Pause("자동 감시 꺼짐");
            try
            {
                if (!preview)
                {
                    var saved = Preferences.Read(preferencesPath);
                    saved.WatchEnabled = enabled;
                    saved.Write(preferencesPath);
                }
                CommitProtectionChanges();
            }
            catch (Exception e)
            {
                if (enabled) Pause("자동 감시 꺼짐");
                status.Text = "화면 보호 설정 저장 실패: " + e.Message;
            }
        }

        private void QueueProtectionChanges()
        {
            if (!protectionReady || refreshingDisplays || IsDisposed) return;
            protectionTimer.Stop(); protectionTimer.Start();
        }

        private bool CommitProtectionChanges()
        {
            protectionTimer.Stop();
            if (!protectionReady || IsDisposed || diagnosing) return false;
            int delay;
            if (!TryGetSeconds(out delay)) { ValidateWait(); status.Text = "대기 시간 설정을 확인해 주세요."; return false; }
            try
            {
                if (!preview) SavePreferences();
                CancelAutomaticStart();
                if (preview) { status.Text = watchEnabled ? "자동 감시 켜짐 · 미리보기" : "자동 감시 꺼짐"; return true; }
                if (!watchEnabled) return true;
                // Reconcile preserves unaffected monitors and their idle timers.
                // Recreating the entire group on every edit would reset other screens.
                displayRecovery.Configure(delay, ignoreWallpaper.IsChecked == true);
                ReconcileDisplays();
                reconnectTimer.Start();
                UpdateWatchingStatus();
                return true;
            }
            catch (Exception e) { status.Text = "화면 보호 설정 저장 실패: " + e.Message; return false; }
        }
    }
}
