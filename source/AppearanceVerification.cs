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
                check(DayPalette.MasterWarmthAt(9) == 0 && DayPalette.MasterWarmthAt(14) == 0 && DayPalette.MasterWarmthAt(20) == 1 && DayPalette.MasterWarmthAt(0) == 1, "Brightness card stays green by day and warm at night");
                double previousWarmth = DayPalette.MasterWarmthAt(0); bool smoothWarmth = true;
                for (int minute = 1; minute <= 1440; minute++)
                {
                    double warmth = DayPalette.MasterWarmthAt(minute / 60.0);
                    smoothWarmth &= warmth >= 0 && warmth <= 1 && Math.Abs(warmth - previousWarmth) < .007;
                    if (minute > 300 && minute <= 540) smoothWarmth &= warmth <= previousWarmth;
                    if (minute > 840 && minute <= 1200) smoothWarmth &= warmth >= previousWarmth;
                    previousWarmth = warmth;
                }
                check(smoothWarmth && DayPalette.MasterWarmthAt(-1) == DayPalette.MasterWarmthAt(23), "Card warmth fades monotonically at dawn/dusk and wraps without a midnight jump");
                double minText = 100, minMuted = 100;
                for (int minute = 0; minute < 1440; minute++)
                {
                    var p = DayPalette.At(minute / 60.0);
                    minText = Math.Min(minText, Math.Min(DayPalette.Contrast(p.Text, p.Background), DayPalette.Contrast(p.Text, p.Panel)));
                    minMuted = Math.Min(minMuted, Math.Min(DayPalette.Contrast(p.Muted, p.Background), DayPalette.Contrast(p.Muted, p.Panel)));
                }
                check(minText >= 4.5 && minMuted >= 4.5, "Text contrast stays at least 4.5:1 across every minute of the day (" + minText.ToString("0.00") + "/" + minMuted.ToString("0.00") + ")");
                var preferences = new AppearancePreferences();
                check(preferences.Language == "KO", "Default interface language is Korean");
                preferences.Language = "EN"; preferences.Normalize(); check(preferences.Language == "EN", "English is a supported language");
                preferences.Language = "invalid"; preferences.Normalize(); check(preferences.Language == "KO", "Unknown language normalizes to Korean");
                UiText.Language = "EN"; check(UiText.Translate("밝기 동기화") == "Brightness sync" && UiText.Translate("MonitorShade · 2대 감시") == "MonitorShade · 2 monitoring", "Native UI messages use the same English catalog");
                UiText.Language = "KO"; check(UiText.Translate("밝기 동기화") == "밝기 동기화", "Korean messages remain unchanged");
                check(preferences.Blur && preferences.Hour(new DateTime(2026, 10, 21, 9, 30, 0)) == 9.5, "New settings use local system time and enable backdrop by default");
                preferences.Theme = "Evening"; check(preferences.Hour(DateTime.MinValue) == 20, "Fixed evening palette does not depend on current time");
                preferences.Theme = "invalid"; preferences.Normalize(); check(preferences.Theme == "Auto", "Unknown theme settings safely normalize");
                check(preferences.KoreanFont == "Pretendard", "New settings use the Pretendard design font");
                preferences.KoreanFont = "MalgunGothic"; preferences.Normalize(); check(preferences.KoreanFont == "MalgunGothic", "Windows font remains an explicit alternative");
                preferences.KoreanFont = "invalid"; preferences.Normalize(); check(preferences.KoreanFont == "Pretendard", "Unknown Korean font settings safely normalize");
                string settingsPath = path + ".settings.xml";
                try
                {
                    File.WriteAllText(settingsPath, "<Preferences><WaitValue>30</WaitValue><Minutes>false</Minutes></Preferences>");
                    var restored = Preferences.Read(settingsPath); check(restored.WaitValue == 30 && restored.Appearance.Blur && restored.Appearance.Theme == "Auto", "Version 1.10 settings migrate without losing protection delay");
                    check(restored.Appearance.KoreanFont == "Pretendard", "Older settings without a font choice adopt the Pretendard design font");
                    check(restored.Appearance.Language == "KO", "Existing settings remain Korean after migration");
                    restored.Appearance.Language = "EN";
                    restored.Appearance.Blur = false; restored.Appearance.Theme = "Morning"; restored.Write(settingsPath);
                    restored = Preferences.Read(settingsPath); check(!restored.Appearance.Blur && restored.Appearance.Theme == "Morning" && restored.WaitValue == 30, "Appearance settings round-trip alongside protection settings");
                    check(restored.Appearance.Language == "EN", "Language persists across settings reload");
                    restored.Appearance.KoreanFont = "Pretendard"; restored.Write(settingsPath);
                    restored = Preferences.Read(settingsPath); check(restored.Appearance.KoreanFont == "Pretendard" && !restored.Appearance.Blur && restored.WaitValue == 30, "Font selection persists without changing blur or protection settings");
                    File.WriteAllText(settingsPath, "<Preferences><WaitValue>45</WaitValue><Appearance><Blur>true</Blur><Theme>Evening</Theme><KoreanFont>MalgunGothic</KoreanFont><TypographyVersion>2</TypographyVersion></Appearance></Preferences>");
                    restored = Preferences.Read(settingsPath);
                    check(restored.Appearance.KoreanFont == "Pretendard" && restored.Appearance.TypographyVersion == 3 && restored.Appearance.Blur && restored.Appearance.Theme == "Evening" && restored.WaitValue == 45, "Version 1.15.1 design font is restored once without changing blur, theme or protection settings");
                    restored.Appearance.KoreanFont = "MalgunGothic"; restored.Write(settingsPath);
                    restored = Preferences.Read(settingsPath);
                    check(restored.Appearance.KoreanFont == "MalgunGothic", "Explicit font choices after migration survive restart");
                }
                finally { if (File.Exists(settingsPath)) File.Delete(settingsPath); }
                File.WriteAllLines(path, results); return 0;
            }
            catch (Exception e) { results.Add("FAIL: " + e.Message); File.WriteAllLines(path, results); return 1; }
        }
    }
}
