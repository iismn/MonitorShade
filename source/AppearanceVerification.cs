using System;
using System.Collections.Generic;
using System.IO;

namespace MonitorShade
{
    internal static class AppearanceVerification
    {
        internal static int Run(string path)
        {
            var results = new List<string>();
            Action<bool,string> check = (valid, text) => { results.Add((valid ? "PASS: " : "FAIL: ") + text); if (!valid) throw new Exception(text); };
            try
            {
                var morning = DayPalette.At(9); var evening = DayPalette.At(20);
                check(morning.Light && morning.Background.R > 240 && morning.Background.G > 240 && morning.Aura.R > morning.Aura.B, "09:00 is warm white with an apricot tint");
                check(!evening.Light && evening.Accent.G > evening.Accent.R && evening.Accent.G > evening.Accent.B, "20:00 retains the olive evening palette");
                check(DayPalette.At(0).Background == DayPalette.At(24).Background && DayPalette.At(-1).Background == DayPalette.At(23).Background, "Palette wraps continuously at midnight");
                double minText = 100, minMuted = 100;
                for (int minute = 0; minute < 1440; minute++)
                {
                    var p = DayPalette.At(minute / 60.0);
                    minText = Math.Min(minText, Math.Min(DayPalette.Contrast(p.Text, p.Background), DayPalette.Contrast(p.Text, p.Panel)));
                    minMuted = Math.Min(minMuted, Math.Min(DayPalette.Contrast(p.Muted, p.Background), DayPalette.Contrast(p.Muted, p.Panel)));
                }
                check(minText >= 4.5 && minMuted >= 4.5, "Text contrast stays at least 4.5:1 across every minute of the day (" + minText.ToString("0.00") + "/" + minMuted.ToString("0.00") + ")");
                var preferences = new AppearancePreferences();
                check(preferences.Blur && preferences.Hour(new DateTime(2026, 10, 21, 9, 30, 0)) == 9.5, "New settings use local system time and enable backdrop by default");
                preferences.Theme = "Evening"; check(preferences.Hour(DateTime.MinValue) == 20, "Fixed evening palette does not depend on current time");
                preferences.Theme = "invalid"; preferences.Normalize(); check(preferences.Theme == "Auto", "Unknown theme settings safely normalize");
                check(preferences.KoreanFont == "MalgunGothic", "New settings use the Windows Korean UI font");
                preferences.KoreanFont = "Pretendard"; preferences.Normalize(); check(preferences.KoreanFont == "Pretendard", "Existing design font can be selected explicitly");
                preferences.KoreanFont = "invalid"; preferences.Normalize(); check(preferences.KoreanFont == "MalgunGothic", "Unknown Korean font settings safely normalize");
                string settingsPath = path + ".settings.xml";
                try
                {
                    File.WriteAllText(settingsPath, "<Preferences><WaitValue>30</WaitValue><Minutes>false</Minutes></Preferences>");
                    var restored = Preferences.Read(settingsPath); check(restored.WaitValue == 30 && restored.Appearance.Blur && restored.Appearance.Theme == "Auto", "Version 1.10 settings migrate without losing protection delay");
                    check(restored.Appearance.KoreanFont == "MalgunGothic", "Older settings without a font choice adopt the Windows Korean UI font");
                    restored.Appearance.Blur = false; restored.Appearance.Theme = "Morning"; restored.Write(settingsPath);
                    restored = Preferences.Read(settingsPath); check(!restored.Appearance.Blur && restored.Appearance.Theme == "Morning" && restored.WaitValue == 30, "Appearance settings round-trip alongside protection settings");
                    restored.Appearance.KoreanFont = "Pretendard"; restored.Write(settingsPath);
                    restored = Preferences.Read(settingsPath); check(restored.Appearance.KoreanFont == "Pretendard" && !restored.Appearance.Blur && restored.WaitValue == 30, "Font selection persists without changing blur or protection settings");
                    File.WriteAllText(settingsPath, "<Preferences><WaitValue>45</WaitValue><Appearance><Blur>true</Blur><Theme>Evening</Theme><KoreanFont>Pretendard</KoreanFont><TypographyVersion>1</TypographyVersion></Appearance></Preferences>");
                    restored = Preferences.Read(settingsPath);
                    check(restored.Appearance.KoreanFont == "MalgunGothic" && restored.Appearance.TypographyVersion == 2 && restored.Appearance.Blur && restored.Appearance.Theme == "Evening" && restored.WaitValue == 45, "Version 1.15 font migrates once without changing blur, theme or protection settings");
                    restored.Appearance.KoreanFont = "Pretendard"; restored.Write(settingsPath);
                    restored = Preferences.Read(settingsPath);
                    check(restored.Appearance.KoreanFont == "Pretendard", "Explicit font choices after migration survive restart");
                }
                finally { if (File.Exists(settingsPath)) File.Delete(settingsPath); }
                File.WriteAllLines(path, results); return 0;
            }
            catch (Exception e) { results.Add("FAIL: " + e.Message); File.WriteAllLines(path, results); return 1; }
        }
    }
}
