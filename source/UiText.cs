using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MonitorShade
{
    internal static class UiText
    {
        internal static string Language = "KO";
        internal static readonly Dictionary<string, string> English = Read();
        private static readonly Regex Fragments = new Regex(string.Join("|", English.Keys.Where(k => k.Length > 1 && Regex.IsMatch(k, "[가-힣]")).OrderByDescending(k => k.Length).Select(Regex.Escape)), RegexOptions.Compiled);
        private static Dictionary<string, string> Read()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MonitorShade.Web.en.json"))
            using (var reader = new StreamReader(stream)) return new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
        }
        internal static string Translate(string text)
        {
            if (Language != "EN" || string.IsNullOrEmpty(text)) return text;
            string value;
            return English.TryGetValue(text, out value) ? value : Fragments.Replace(text, m => English[m.Value]);
        }
    }

    internal sealed partial class SettingsForm
    {
        private void LocalizeTray()
        {
            if (tray == null) return;
            tray.ContextMenuStrip.Items[0].Text = UiText.Translate("설정 열기");
            tray.ContextMenuStrip.Items[3].Text = UiText.Translate("종료");
            pauseItem.Text = UiText.Translate(watchEnabled ? "감시 일시정지" : "감시 시작 / 다시 시작");
            tray.Text = "MonitorShade";
        }
    }
}
