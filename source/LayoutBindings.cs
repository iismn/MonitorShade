using System;
using System.Linq;

namespace MonitorShade
{
    public sealed class LayoutShortcut
    {
        public string Action;
        public int Modifiers;
        public int Key;
        public LayoutShortcut() { }
        public LayoutShortcut(string action, int modifiers, int key) { Action = action; Modifiers = modifiers; Key = key; }
    }

    // Modifiers use the Win32 MOD_* values: Alt=1, Ctrl=2, Shift=4, Win=8.
    // Published arrays are snapshots. The hook never reads the mutable editor draft.
    internal static class LayoutBindings
    {
        internal static readonly string[] Actions = { "Left", "Up", "Right", "Down", "Center", "Scale" };
        internal static LayoutShortcut[] Defaults()
        { return Actions.Select((a, i) => new LayoutShortcut(a, 8, i < 4 ? 0x25 + i : i == 4 ? 0x2E : 0x08)).ToArray(); }
        internal static LayoutShortcut[] Copy(LayoutShortcut[] bindings)
        { return bindings.Select(b => new LayoutShortcut(b.Action, b.Modifiers, b.Key)).ToArray(); }
        internal static bool ValidKey(int key)
        { return (key >= 0x30 && key <= 0x39) || (key >= 0x41 && key <= 0x5A) || (key >= 0x70 && key <= 0x7B) || (key >= 0x21 && key <= 0x28) || key == 0x08 || key == 0x2D || key == 0x2E; }
        internal static string Error(LayoutShortcut[] bindings, BrightnessBinding brightness)
        {
            if (bindings == null || bindings.Length != Actions.Length || bindings.Any(b => b == null || !Actions.Contains(b.Action)) || bindings.Select(b => b.Action).Distinct().Count() != Actions.Length)
                return "여섯 동작의 단축키를 지정하세요.";
            foreach (var b in bindings)
            {
                if (b.Modifiers < 1 || b.Modifiers > 15 || b.Modifiers == 4 || !ValidKey(b.Key)) return "Ctrl, Alt 또는 Win이 포함된 키 조합을 선택하세요.";
                if ((b.Modifiers == 3 && b.Key >= 0x78 && b.Key <= 0x7B) || (b.Modifiers == 3 && b.Key == 0x2E) || (b.Modifiers == 8 && b.Key == 0x4C) || (b.Modifiers == 1 && b.Key == 0x73))
                    return "Windows 또는 공통 단축키에서 사용하는 조합입니다.";
                if (brightness != null && brightness.Enabled && (b.Key == brightness.Down || b.Key == brightness.Up))
                {
                    // Right Alt can emit Ctrl+Alt on AltGr keyboards.
                    int modifier = brightness.Modifier == "CtrlShift" ? 6 : brightness.Modifier == "CtrlAlt" ? 3 : 1;
                    if (b.Modifiers == modifier || (brightness.Modifier == "RightAlt" && b.Modifiers == 3)) return "밝기 단축키와 겹칩니다. 다른 키 조합을 선택하세요.";
                }
            }
            if (bindings.Select(b => b.Modifiers + ":" + b.Key).Distinct().Count() != bindings.Length) return "같은 단축키를 두 동작에 지정할 수 없습니다.";
            return null;
        }
        internal static LayoutShortcut[] Normalize(LayoutShortcut[] bindings)
        { return Error(bindings, null) == null ? Copy(bindings) : Defaults(); }
    }
}
