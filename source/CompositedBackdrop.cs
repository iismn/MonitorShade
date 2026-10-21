using System;
using System.Drawing;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using W = System.Windows;
using C = System.Windows.Controls;
using F = System.Windows.Forms;
using M = System.Windows.Media;
using I = System.Windows.Media.Imaging;

namespace MonitorShade
{
    internal sealed class CompositedBackdrop : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct Transform { internal float M11, M12, M13, M21, M22, M23, M31, M32, M33; }
        [DllImport("Magnification.dll")] private static extern bool MagInitialize();
        [DllImport("Magnification.dll")] private static extern bool MagUninitialize();
        [DllImport("Magnification.dll")] private static extern bool MagSetWindowSource(IntPtr window, Rect source);
        [DllImport("Magnification.dll")] private static extern bool MagSetWindowTransform(IntPtr window, ref Transform transform);
        [DllImport("Magnification.dll")] private static extern bool MagSetWindowFilterList(IntPtr window, uint mode, int count, IntPtr[] windows);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(int ex, string className, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr window, uint color, byte alpha, uint flags);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr window, IntPtr rect, bool erase);
        private delegate bool EnumWindow(IntPtr window, IntPtr state);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr state);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

        private sealed class Helper : F.Form
        {
            [StructLayout(LayoutKind.Sequential)] private struct Position { internal IntPtr Window, After; internal int X, Y, Width, Height; internal uint Flags; }
            private readonly IntPtr above;
            internal Helper(IntPtr above) { this.above = above; FormBorderStyle = F.FormBorderStyle.None; ShowInTaskbar = false; StartPosition = F.FormStartPosition.Manual; AutoScaleMode = F.AutoScaleMode.None; }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override F.CreateParams CreateParams
            { get { var p = base.CreateParams; p.ExStyle |= 0x080800a0; return p; } } // no activate, layered, tool window, click-through
            protected override void WndProc(ref F.Message message)
            {
                if (message.Msg == 0x0046 && message.LParam != IntPtr.Zero)
                {
                    // Keep the sampler below the app even during its first Show.
                    var position = (Position)Marshal.PtrToStructure(message.LParam, typeof(Position));
                    position.After = above; position.Flags = (position.Flags & ~4u) | 0x10u;
                    Marshal.StructureToPtr(position, message.LParam, false);
                }
                base.WndProc(ref message);
            }
        }
        private readonly SettingsWindow window;
        private readonly Func<bool> suspended;
        private readonly Action changed;
        private readonly C.Image image = new C.Image { Stretch = M.Stretch.Fill, IsHitTestVisible = false };
        private readonly C.Border tint = new C.Border { IsHitTestVisible = false };
        private readonly W.Threading.DispatcherTimer timer;
        private Helper helper;
        private BackdropCapture capture;
        private IntPtr magnifier;
        private bool initialized, requested, disposed, dark;
        private int revision, frameCount, posting;
        private I.BitmapSource pendingFrame;
        private readonly uint processId = (uint)Process.GetCurrentProcess().Id;
        private IntPtr[] excluded = new IntPtr[0];
        private Rect sourceRect;
        private Size sampleSize;
        private DateTime retryAt, receivedAt;
        internal readonly C.Grid Root = new C.Grid();
        internal string Diagnostic { get { return sourceRect.Left + "," + sourceRect.Top + ".." + sourceRect.Right + "," + sourceRect.Bottom + "; frames=" + frameCount + "; helper=" + (helper == null ? "none" : helper.Bounds.ToString()); } }
        internal bool Active { get; private set; }
        internal string Error { get; private set; }
        internal int FrameCount { get { return frameCount; } }
        internal bool Running { get { return capture != null; } }
        internal I.BitmapSource BackgroundFrame { get { return image.Source as I.BitmapSource; } }

        internal CompositedBackdrop(SettingsWindow window, C.UserControl ui, Func<bool> suspended, Action changed)
        {
            this.window = window; this.suspended = suspended; this.changed = changed;
            image.Effect = new M.Effects.BlurEffect { Radius = 30, RenderingBias = M.Effects.RenderingBias.Performance };
            Root.Children.Add(image); Root.Children.Add(tint); Root.Children.Add(ui);
            timer = new W.Threading.DispatcherTimer(W.Threading.DispatcherPriority.Background, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += Tick;
            window.IsVisibleChanged += VisibilityChanged; window.StateChanged += StateChanged;
        }

        internal bool Apply(bool enabled, bool isDark)
        {
            bool wasRequested = requested;
            requested = enabled && DesktopBackdrop.TransparencyEnabled; dark = isDark;
            if (requested && !wasRequested) retryAt = DateTime.MinValue;
            UpdateBase();
            if (!requested) { timer.Stop(); Stop(); image.Source = null; Active = false; Error = ""; SetSurface(); return false; }
            if (window.IsVisible && window.WindowState != W.WindowState.Minimized)
            {
                timer.Start(); Refresh();
            }
            SetSurface(); return Active;
        }
        private void UpdateBase()
        {
            // Acrylic's luminance tint is below the original Page (194/255) and
            // Card (210/255) brushes. No text/card is flattened into a solid patch.
            tint.Background = new M.SolidColorBrush(M.Color.FromArgb(204, dark ? (byte)32 : (byte)252, dark ? (byte)32 : (byte)252, dark ? (byte)32 : (byte)252));
        }
        private void SetSurface()
        {
            bool opaque = Active || !requested;
            Root.Background = opaque ? (dark ? M.Brushes.Black : M.Brushes.White) : M.Brushes.Transparent;
            image.Visibility = tint.Visibility = Active ? W.Visibility.Visible : W.Visibility.Collapsed;
        }
        private void Tick(object sender, EventArgs e) { Refresh(); }
        private void VisibilityChanged(object sender, W.DependencyPropertyChangedEventArgs e) { VisibilityChanged(); }
        private void StateChanged(object sender, EventArgs e) { VisibilityChanged(); }
        private void VisibilityChanged()
        {
            if (!window.IsVisible || window.WindowState == W.WindowState.Minimized) { timer.Stop(); Stop(); image.Source = null; return; }
            if (requested) { timer.Start(); Refresh(); }
        }
        private void Refresh()
        {
            if (disposed || !requested || !window.IsVisible || window.WindowState == W.WindowState.Minimized) return;
            if (suspended() || !DesktopAccess.Available()) { Stop(); image.Source = null; return; }
            if (DateTime.UtcNow < retryAt) return;
            try
            {
                IntPtr hwnd = window.Handle; Rect bounds;
                int cloaked;
                if (!IsWindowVisible(hwnd) || (DwmGetWindowAttribute(hwnd, 14, out cloaked, 4) == 0 && cloaked != 0)) { Stop(); image.Source = null; return; }
                if (!GetClientRect(hwnd, out bounds) || bounds.Right < 100 || bounds.Bottom < 100) return;
                var origin = new Point(); if (!ClientToScreen(hwnd, ref origin)) return;
                // Downsample before capture: about 300x220 pixels for the normal window.
                int width = Math.Min(1024, (bounds.Right + 3) / 4), height = Math.Min(1024, (bounds.Bottom + 3) / 4);
                sourceRect = new Rect { Left = origin.X, Top = origin.Y, Right = origin.X + bounds.Right, Bottom = origin.Y + bounds.Bottom };
                if (capture != null && (sampleSize.Width != width || sampleSize.Height != height)) Stop();
                if (capture == null)
                {
                    if (!initialized) { if (!MagInitialize()) throw new InvalidOperationException("배경 합성을 시작할 수 없습니다."); initialized = true; }
                    helper = new Helper(hwnd) { BackColor = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(252, 252, 252), Bounds = new Rectangle(origin.X + 12, origin.Y + 48, width, height) };
                    helper.Show(); SetLayeredWindowAttributes(helper.Handle, 0, 255, 2);
                    Native.SetWindowPos(helper.Handle, hwnd, origin.X + 12, origin.Y + 48, width, height, 0x0010);
                    magnifier = CreateWindowEx(0, "Magnifier", "", 0x50000000, 0, 0, width, height, helper.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    if (magnifier == IntPtr.Zero) throw new InvalidOperationException("배경 표면을 만들 수 없습니다.");
                    var transform = new Transform { M11 = width / (float)bounds.Right, M22 = height / (float)bounds.Bottom, M33 = 1 };
                    if (!MagSetWindowTransform(magnifier, ref transform)) throw new InvalidOperationException("배경 크기를 설정할 수 없습니다.");
                    UpdateFilter();
                    MagSetWindowSource(magnifier, sourceRect); sampleSize = new Size(width, height);
                    int token = ++revision; receivedAt = DateTime.UtcNow;
                    capture = new BackdropCapture(helper.Handle, bitmap => PostFrame(token, bitmap), error => PostFailure(token, error));
                    Active = true; Error = ""; SetSurface(); changed();
                }
                Native.SetWindowPos(helper.Handle, hwnd, origin.X + 12, origin.Y + 48, width, height, 0x0010);
                UpdateFilter();
                if (!MagSetWindowSource(magnifier, sourceRect)) throw new InvalidOperationException("배경 위치를 갱신할 수 없습니다.");
                InvalidateRect(magnifier, IntPtr.Zero, true);
                if (DateTime.UtcNow - receivedAt > TimeSpan.FromSeconds(5)) throw new InvalidOperationException("배경 프레임이 응답하지 않습니다.");
            }
            catch (Exception e) { Fail(e.Message); }
        }
        private void PostFrame(int token, I.BitmapSource frame)
        {
            // Keep only the newest frame while WPF is busy; never accumulate bitmaps
            // or UI callbacks during resizing or a long page layout.
            System.Threading.Interlocked.Exchange(ref pendingFrame, frame);
            if (System.Threading.Interlocked.Exchange(ref posting, 1) != 0) return;
            window.Dispatcher.BeginInvoke(W.Threading.DispatcherPriority.Background, new Action(delegate {
                var latest = System.Threading.Interlocked.Exchange(ref pendingFrame, null);
                System.Threading.Interlocked.Exchange(ref posting, 0);
                if (disposed || token != revision || capture == null) return;
                if (latest != null) { image.Source = latest; receivedAt = DateTime.UtcNow; frameCount++; }
            }));
        }
        private void UpdateFilter()
        {
            var windows = new List<IntPtr>();
            EnumWindows(delegate(IntPtr candidate, IntPtr state) { uint owner; GetWindowThreadProcessId(candidate, out owner); if (owner == processId) windows.Add(candidate); return true; }, IntPtr.Zero);
            var current = windows.OrderBy(w => w.ToInt64()).ToArray();
            if (current.SequenceEqual(excluded)) return;
            if (!MagSetWindowFilterList(magnifier, 0, current.Length, current)) throw new InvalidOperationException("자기 창을 배경에서 제외할 수 없습니다.");
            excluded = current;
        }
        private void PostFailure(int token, string error)
        {
            window.Dispatcher.BeginInvoke(new Action(delegate { if (!disposed && token == revision) Fail(error); }));
        }
        private void Fail(string error)
        {
            Stop(); image.Source = null; Active = false; Error = error; retryAt = DateTime.UtcNow.AddSeconds(30); SetSurface(); changed();
        }
        private void Stop()
        {
            ++revision;
            if (capture != null) { capture.Dispose(); capture = null; }
            if (magnifier != IntPtr.Zero) { DestroyWindow(magnifier); magnifier = IntPtr.Zero; }
            if (helper != null) { helper.Dispose(); helper = null; }
            excluded = new IntPtr[0]; System.Threading.Interlocked.Exchange(ref pendingFrame, null);
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            timer.Stop(); timer.Tick -= Tick;
            window.IsVisibleChanged -= VisibilityChanged; window.StateChanged -= StateChanged;
            Stop(); image.Source = null; if (initialized) MagUninitialize();
        }
    }
}
