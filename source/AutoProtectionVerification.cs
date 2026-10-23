using System;
using System.Threading.Tasks;
using C = System.Windows.Controls;

namespace MonitorShade
{
    internal sealed partial class SettingsForm
    {
        internal async Task VerifyAutoProtection(Action<bool, string> check)
        {
            if (!preview) throw new InvalidOperationException("Protection UI verification requires preview mode.");
            bool oldEnabled = watchEnabled, oldMinutes = minutes.IsChecked == true;
            string oldAmount = amount.Text;
            try
            {
                ChangeMonitoring(true);
                OpenSettings();
                check(watchEnabled && Find<C.CheckBox>("WatchEnabled").IsChecked == true && IsVisible, "Opening settings preserves enabled monitoring");
                seconds.IsChecked = true; amount.Text = "7";
                await Task.Delay(650);
                check(!protectionTimer.Enabled && watchEnabled && status.Text == "자동 감시 켜짐 · 미리보기", "Timeout edits apply automatically after debounce without pressing Start");
                amount.Text = "0"; await Task.Delay(650);
                check(validation.Visibility == System.Windows.Visibility.Visible, "Invalid automatic timeout changes remain visibly rejected");
                ChangeMonitoring(false);
                check(!watchEnabled && !displayRecovery.Enabled && !startupPending && !protectionTimer.Enabled, "Toggle off stops monitoring even with an invalid timeout");
                amount.Text = "8"; await Task.Delay(650);
                OpenSettings();
                check(!watchEnabled && Find<C.CheckBox>("WatchEnabled").IsChecked == false, "Editing a timeout and reopening settings cannot undo an explicit pause");
                ChangeMonitoring(true); Hide(); Show();
                check(watchEnabled && IsVisible, "Hiding and reopening settings preserves enabled monitoring");
            }
            finally
            {
                amount.Text = oldAmount; minutes.IsChecked = oldMinutes; seconds.IsChecked = !oldMinutes;
                ChangeMonitoring(oldEnabled); protectionTimer.Stop();
            }
        }
    }
}
