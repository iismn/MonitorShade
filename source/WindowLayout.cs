using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MonitorShade
{
    internal enum LayoutDirection { Left, Up, Right, Down, Center, Scale }

    internal static class LayoutGeometry
    {
        internal static Rectangle Bounds(Rectangle area, LayoutDirection direction, int step, int percent)
        {
            if (area.Width <= 0 || area.Height <= 0) throw new ArgumentException("Invalid work area");
            if (direction == LayoutDirection.Scale)
            {
                if (step == 2) return area;
                double scale = step == 0 ? .5 : .7;
                int scaledWidth = Math.Max(1, (int)Math.Round(area.Width * scale));
                int scaledHeight = Math.Max(1, (int)Math.Round(area.Height * scale));
                return new Rectangle(area.X + (area.Width - scaledWidth) / 2, area.Y + (area.Height - scaledHeight) / 2, scaledWidth, scaledHeight);
            }
            if (direction == LayoutDirection.Center)
            {
                if (step == 1) return area;
                double width = Math.Min(area.Width * Math.Max(10, Math.Min(100, percent)) / 100.0, area.Height * 16.0 / 9.0);
                int w = Math.Max(1, (int)Math.Round(width)), h = Math.Max(1, (int)Math.Round(width * 9 / 16));
                return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
            }
            if (step == 2) return area;
            double fraction = step == 0 ? 0.5 : 2.0 / 3.0;
            if (direction == LayoutDirection.Left || direction == LayoutDirection.Right)
            {
                int width = (int)Math.Round(area.Width * fraction);
                return new Rectangle(direction == LayoutDirection.Left ? area.Left : area.Right - width, area.Top, width, area.Height);
            }
            int height = (int)Math.Round(area.Height * fraction);
            return new Rectangle(area.Left, direction == LayoutDirection.Up ? area.Top : area.Bottom - height, area.Width, height);
        }
        internal static bool Near(Rectangle a, Rectangle b)
        { return Math.Abs(a.X - b.X) <= 3 && Math.Abs(a.Y - b.Y) <= 3 && Math.Abs(a.Width - b.Width) <= 3 && Math.Abs(a.Height - b.Height) <= 3; }
    }

    // Tracks only the relevant key states. No text is collected or persisted.
    internal sealed class LayoutKeys
    {
        private readonly HashSet<int> down = new HashSet<int>();
        private readonly HashSet<int> swallowed = new HashSet<int>();
        internal void Seed(int key) { down.Add(key); }
        internal bool Process(int key, bool pressed, bool eligible, out LayoutDirection? command)
        {
            command = null;
            bool relevant = key == 0x5B || key == 0x5C || (key >= 0x25 && key <= 0x28) || key == 0x2E || key == 0x08 || (key >= 0x10 && key <= 0x12) || (key >= 0xA0 && key <= 0xA5);
            if (!relevant) return false;
            bool repeat = pressed && down.Contains(key);
            if (pressed) down.Add(key); else down.Remove(key);
            if (swallowed.Contains(key)) { if (!pressed) swallowed.Remove(key); return true; }
            if (!pressed || repeat || !eligible || !(down.Contains(0x5B) || down.Contains(0x5C))) return false;
            foreach (int modifier in new[] { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 })
                if (down.Contains(modifier)) return false;
            if (key >= 0x25 && key <= 0x28) command = (LayoutDirection)(key - 0x25);
            else if (key == 0x2E) command = LayoutDirection.Center;
            else if (key == 0x08) command = LayoutDirection.Scale;
            if (!command.HasValue) return false;
            swallowed.Add(key); return true;
        }
    }

    internal sealed class LayoutKeyboardHook : IDisposable
    {
        private readonly Thread thread;
        private readonly ManualResetEvent ready = new ManualResetEvent(false);
        private readonly Action<IntPtr, LayoutDirection> dispatch;
        private readonly LayoutNative.HookProc callback;
        private readonly LayoutKeys keys = new LayoutKeys();
        private readonly BrightnessKeys brightnessKeys = new BrightnessKeys();
        private readonly Action<int> brightnessDispatch;
        private volatile BrightnessBinding brightnessBinding;
        internal void ConfigureBrightness(BrightnessBinding binding) { brightnessBinding = binding; }
        private IntPtr hook;
        private uint threadId;
        private Exception startupError;
        private bool disposed;
        private volatile bool enabled;
        internal bool Enabled { get { return enabled; } set { enabled = value; } }
        internal bool Installed { get { return hook != IntPtr.Zero; } }
        internal LayoutKeyboardHook(Action<IntPtr, LayoutDirection> action, Action<int> brightnessAction = null)
        {
            dispatch = action; brightnessDispatch = brightnessAction; callback = OnKey;
            thread = new Thread(Run) { IsBackground = true, Name = "MonitorShade shortcuts" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            ready.WaitOne();
            if (startupError != null) { ready.Dispose(); throw startupError; }
        }
        private void Run()
        {
            try
            {
                threadId = LayoutNative.GetCurrentThreadId();
                LayoutNative.MSG message;
                LayoutNative.PeekMessage(out message, IntPtr.Zero, 0, 0, 0);
                foreach (int key in new[] { 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 })
                    if ((LayoutNative.GetAsyncKeyState(key) & 0x8000) != 0) { keys.Seed(key); brightnessKeys.Seed(key); }
                hook = LayoutNative.SetWindowsHookEx(13, callback, LayoutNative.GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                ready.Set();
                while (LayoutNative.GetMessage(out message, IntPtr.Zero, 0, 0) > 0) { LayoutNative.TranslateMessage(ref message); LayoutNative.DispatchMessage(ref message); }
            }
            catch (Exception e) { startupError = e; ready.Set(); }
            finally { if (hook != IntPtr.Zero) { LayoutNative.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }
        }
        private IntPtr OnKey(int code, IntPtr kind, IntPtr data)
        {
            if (code < 0) return LayoutNative.CallNextHookEx(hook, code, kind, data);
            LayoutNative.KEYBOARD key = (LayoutNative.KEYBOARD)Marshal.PtrToStructure(data, typeof(LayoutNative.KEYBOARD));
            bool injected = (key.flags & 0x10) != 0;
            if (injected && key.vk == 0xE8) return LayoutNative.CallNextHookEx(hook, code, kind, data);
            bool pressed = kind.ToInt32() == 0x100 || kind.ToInt32() == 0x104;
            bool released = kind.ToInt32() == 0x101 || kind.ToInt32() == 0x105;
            if (!pressed && !released) return LayoutNative.CallNextHookEx(hook, code, kind, data);
            LayoutDirection? command = null;
            int normalizedKey = BrightnessKeys.Normalize((int)key.vk, key.scan, key.flags);
            int brightnessDirection;
            bool brightnessConsume = brightnessKeys.Process(normalizedKey, pressed, brightnessBinding, out brightnessDirection);
            IntPtr window = LayoutNative.GetForegroundWindow();
            bool shortcutKey = (key.vk >= 0x25 && key.vk <= 0x28) || key.vk == 0x2E || key.vk == 0x08;
            // Brightness also accepts accessibility-generated keys. Layout keeps its physical-key policy.
            bool consume = !injected && keys.Process(normalizedKey, pressed, enabled && shortcutKey && LayoutNative.CanArrange(window), out command);
            if (brightnessDirection != 0 && brightnessDispatch != null) { LayoutNative.MaskStartMenu(); try { brightnessDispatch(brightnessDirection); } catch { } }
            if (command.HasValue)
            {
                // A harmless unassigned key prevents the released Win key from opening Start.
                LayoutNative.MaskStartMenu();
                try { dispatch(window, command.Value); } catch { }
            }
            return consume || brightnessConsume ? new IntPtr(1) : LayoutNative.CallNextHookEx(hook, code, kind, data);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; enabled = false;
            LayoutNative.PostThreadMessage(threadId, 0x12, IntPtr.Zero, IntPtr.Zero);
            thread.Join(2000); ready.Dispose();
        }
    }

    internal sealed class WindowArranger : IDisposable
    {
        private sealed class State { internal LayoutDirection Direction; internal int Step, Percent; internal Rectangle Actual, Area; internal uint Process; internal bool Maximized; }
        private sealed class Request { internal IntPtr Window; internal LayoutDirection Direction; internal int Percent; internal State State; internal Rectangle Desired; internal int Phase, Attempts; }
        private readonly Dictionary<IntPtr, State> states = new Dictionary<IntPtr, State>();
        private readonly Queue<Request> queue = new Queue<Request>();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 60 };
        private Request current;
        internal event Action<string> Failed;
        internal bool Busy { get { return current != null || queue.Count > 0; } }
        internal WindowArranger() { timer.Tick += delegate { Tick(); }; }
        internal void Apply(IntPtr window, LayoutDirection direction, int percent)
        {
            if (!LayoutNative.CanArrange(window) || queue.Count >= 24) return;
            queue.Enqueue(new Request { Window = window, Direction = direction, Percent = percent }); timer.Start();
            if (current == null) Tick();
        }
        private void Tick()
        {
            try
            {
                if (current == null)
                {
                    if (queue.Count == 0) { timer.Stop(); return; }
                    current = queue.Dequeue();
                    if (!LayoutNative.CanArrange(current.Window)) { current = null; return; }
                    Rectangle area = Screen.FromHandle(current.Window).WorkingArea, actual = LayoutNative.VisibleBounds(current.Window);
                    uint process; LayoutNative.GetWindowThreadProcessId(current.Window, out process);
                    State previous;
                    int step = states.TryGetValue(current.Window, out previous) && previous.Direction == current.Direction && previous.Area == area && previous.Process == process && previous.Percent == current.Percent && previous.Maximized == LayoutNative.IsZoomed(current.Window) && LayoutGeometry.Near(previous.Actual, actual)
                        ? (previous.Step + 1) % (current.Direction == LayoutDirection.Center ? 2 : 3) : 0;
                    current.State = new State { Direction = current.Direction, Step = step, Percent = current.Percent, Area = area, Process = process, Maximized = step == (current.Direction == LayoutDirection.Center ? 1 : 2) };
                    current.Desired = LayoutGeometry.Bounds(area, current.Direction, step, current.Percent);
                    if (current.State.Maximized)
                    {
                        if (!LayoutNative.ShowWindowAsync(current.Window, 3)) throw new InvalidOperationException("이 창을 최대화할 수 없습니다.");
                        current.Phase = 2;
                    }
                    else
                    {
                        if (LayoutNative.IsZoomed(current.Window) || LayoutNative.IsIconic(current.Window)) LayoutNative.ShowWindowAsync(current.Window, 9);
                        current.Phase = 1;
                    }
                    return;
                }
                if (!LayoutNative.IsWindow(current.Window)) { current = null; return; }
                if (current.Phase == 1)
                {
                    if (LayoutNative.IsZoomed(current.Window)) { if (++current.Attempts > 15) throw new InvalidOperationException("창을 복원하지 못했습니다."); return; }
                    LayoutNative.RECT raw; if (!LayoutNative.GetWindowRect(current.Window, out raw)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    Rectangle outer = raw.Rectangle, visible = LayoutNative.VisibleBounds(current.Window), desired = current.Desired;
                    // DWM's visible bounds exclude the invisible resize border; compensate it.
                    int left = visible.Left - outer.Left, top = visible.Top - outer.Top;
                    int right = outer.Right - visible.Right, bottom = outer.Bottom - visible.Bottom;
                    if (!LayoutNative.SetWindowPos(current.Window, IntPtr.Zero, desired.X - left, desired.Y - top, desired.Width + left + right, desired.Height + top + bottom, 0x4 | 0x10 | 0x4000))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    current.Phase = 2; current.Attempts = 0; return;
                }
                Rectangle result = LayoutNative.VisibleBounds(current.Window);
                bool success = current.State.Maximized ? LayoutNative.IsZoomed(current.Window) : LayoutGeometry.Near(result, current.Desired);
                if (!success && ++current.Attempts < 8) return;
                if (!success) throw new InvalidOperationException("이 앱의 최소 창 크기나 권한 때문에 요청한 크기를 적용하지 못했습니다.");
                current.State.Actual = result;
                if (states.Count > 256) states.Clear();
                states[current.Window] = current.State; current = null;
            }
            catch (Exception e) { if (current != null) states.Remove(current.Window); current = null; if (Failed != null) Failed(e.Message); }
        }
        internal void Reset() { queue.Clear(); current = null; states.Clear(); timer.Stop(); }
        public void Dispose() { Reset(); timer.Dispose(); }
    }

    internal static class LayoutNative
    {
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { internal int Left, Top, Right, Bottom; internal Rectangle Rectangle { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } } }
        [StructLayout(LayoutKind.Sequential)] internal struct KEYBOARD { internal uint vk, scan, flags, time; internal UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct MSG { internal IntPtr window; internal uint message; internal UIntPtr wParam; internal IntPtr lParam; internal uint time; internal Point pt; internal uint reserved; }
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { internal uint type; internal INPUTUNION data; }
        [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION { [FieldOffset(0)] internal KEYINPUT keyboard; [FieldOffset(0)] internal MOUSEINPUT mouse; }
        [StructLayout(LayoutKind.Sequential)] private struct KEYINPUT { internal ushort vk, scan; internal uint flags, time; internal UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { internal int x, y; internal uint data, flags, time; internal UIntPtr extra; }
        internal delegate IntPtr HookProc(int code, IntPtr kind, IntPtr data);
        internal static void MaskStartMenu()
        {
            INPUT[] input = new INPUT[2]; input[0].type = input[1].type = 1;
            input[0].data.keyboard.vk = input[1].data.keyboard.vk = 0xE8; input[1].data.keyboard.flags = 2;
            SendInput(2, input, Marshal.SizeOf(typeof(INPUT)));
        }
        internal static bool CanArrange(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindow(window) || !IsWindowVisible(window) || !IsWindowEnabled(window) || IsHungAppWindow(window)) return false;
            long style = GetWindowLongPtr(window, -16).ToInt64();
            if ((style & 0x40000) == 0 || (style & 0x40000000) != 0) return false;
            StringBuilder name = new StringBuilder(128); GetClassName(window, name, name.Capacity);
            string c = name.ToString();
            return c != "Progman" && c != "WorkerW" && c != "Shell_TrayWnd" && c != "Shell_SecondaryTrayWnd";
        }
        internal static Rectangle VisibleBounds(IntPtr window)
        {
            RECT rect;
            if (DwmGetWindowAttribute(window, 9, out rect, Marshal.SizeOf(typeof(RECT))) == 0 && rect.Right > rect.Left) return rect.Rectangle;
            if (!GetWindowRect(window, out rect)) throw new Win32Exception(Marshal.GetLastWin32Error()); return rect.Rectangle;
        }
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr kind, IntPtr data);
        [DllImport("user32.dll")] internal static extern int GetMessage(out MSG msg, IntPtr window, uint min, uint max);
        [DllImport("user32.dll")] internal static extern bool PeekMessage(out MSG msg, IntPtr window, uint min, uint max, uint remove);
        [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] internal static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")] internal static extern bool PostThreadMessage(uint thread, uint message, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsHungAppWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, StringBuilder name, int maximum);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out RECT value, int size);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    }
}
