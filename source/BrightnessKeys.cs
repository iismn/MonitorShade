using System;
using System.Collections.Generic;

namespace MonitorShade
{
    internal sealed class BrightnessBinding
    {
        internal readonly bool Enabled;
        internal readonly string Modifier;
        internal readonly int Down, Up;
        internal BrightnessBinding(bool enabled, string modifier, int down, int up) { Enabled = enabled; Modifier = modifier; Down = down; Up = up; }
        internal static bool Valid(string modifier, int down, int up)
        {
            return Array.IndexOf(new[] { "RightAlt", "LeftAlt", "CtrlAlt", "CtrlShift" }, modifier) >= 0 && down >= 0x70 && down <= 0x7B && up >= 0x70 && up <= 0x7B && down != up && !((modifier == "CtrlAlt" || modifier == "RightAlt") && (down >= 0x78 || up >= 0x78));
        }
    }
    internal sealed class BrightnessKeys
    {
        private readonly HashSet<int> down = new HashSet<int>(), swallowed = new HashSet<int>();
        internal void Seed(int key) { down.Add(key); }
        internal static int Normalize(int vk, uint scan, uint flags) { return scan == 0x38 && (flags & 1) != 0 ? 0xA5 : vk; }
        internal bool Process(int key, bool pressed, BrightnessBinding binding, out int direction)
        {
            direction = 0;
            if (!(key >= 0x70 && key <= 0x7B) && !(key >= 0xA0 && key <= 0xA5) && key != 0x5B && key != 0x5C && !(key >= 0x10 && key <= 0x12)) return false;
            bool repeat = pressed && down.Contains(key);
            if (pressed) down.Add(key); else down.Remove(key);
            if (swallowed.Contains(key)) { if (!pressed) swallowed.Remove(key); return true; }
            if (!pressed || repeat || binding == null || !binding.Enabled || (key != binding.Down && key != binding.Up) || down.Contains(0x5B) || down.Contains(0x5C)) return false;
            bool ctrl = down.Contains(0x11) || down.Contains(0xA2) || down.Contains(0xA3), shift = down.Contains(0x10) || down.Contains(0xA0) || down.Contains(0xA1);
            bool left = down.Contains(0xA4), right = down.Contains(0xA5);
            bool matches = binding.Modifier == "RightAlt" ? right && !left && !shift : binding.Modifier == "LeftAlt" ? left && !right && !ctrl && !shift : binding.Modifier == "CtrlAlt" ? ctrl && (left || right) && !shift : binding.Modifier == "CtrlShift" && ctrl && shift && !left && !right;
            // AltGr can synthesize Left Ctrl together with Right Alt. RightAlt accepts that chord.
            if (!matches) return false;
            swallowed.Add(key); direction = key == binding.Down ? -1 : 1; return true;
        }
    }
}
