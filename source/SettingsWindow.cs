using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using C = System.Windows.Controls;
using F = System.Windows.Forms;

namespace MonitorShade
{
    // Native HWND shell hosts the transparent Chromium settings surface. The existing
    // WinForms-style lifetime/message interface also serves the tray and monitor controllers.
    internal class SettingsWindow : Window, IDisposable, F.IWin32Window
    {
        private IntPtr handle;
        private HwndSource source;
        internal bool IsDisposed { get; private set; }
        internal bool IsHandleCreated { get { return handle != IntPtr.Zero; } }
        public IntPtr Handle { get { return IsDisposed ? IntPtr.Zero : handle != IntPtr.Zero ? handle : new WindowInteropHelper(this).EnsureHandle(); } }
        internal bool Visible { get { return IsVisible; } }
        internal string Text { get { return Title; } set { Title = value; } }
        internal void BeginInvoke(Action action) { Dispatcher.BeginInvoke(action); }
        protected SettingsWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            // Keep an ordinary DWM window: AllowsTransparency would disable native chrome and crisp text.
            WindowChrome.SetWindowChrome(this, new WindowChrome {
                CaptionHeight = 40, ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false
            });
        }
        protected void ConnectCaption(C.UserControl content)
        {
            var minimize = (C.Button)content.FindName("CaptionMinimize");
            var maximize = (C.Button)content.FindName("CaptionMaximize");
            var close = (C.Button)content.FindName("CaptionClose");
            var glyph = (C.TextBlock)content.FindName("CaptionMaximizeGlyph");
            minimize.Click += delegate { SystemCommands.MinimizeWindow(this); };
            maximize.Click += delegate { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); };
            close.Click += delegate { SystemCommands.CloseWindow(this); };
            Action update = delegate {
                bool maximized = WindowState == WindowState.Maximized;
                glyph.Text = maximized ? "\uE923" : "\uE922";
                maximize.ToolTip = maximized ? "이전 크기로" : "최대화";
                System.Windows.Automation.AutomationProperties.SetName(maximize, maximized ? "이전 크기로" : "최대화");
            };
            StateChanged += delegate { update(); };
            update();
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            handle = new WindowInteropHelper(this).Handle;
            source = HwndSource.FromHwnd(handle);
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            source.AddHook(ProcessMessage);
            OnHandleCreated(e);
        }
        private IntPtr ProcessMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            F.Message message = F.Message.Create(hwnd, msg, wParam, lParam);
            WndProc(ref message); handled = message.Msg == 0;
            return message.Result;
        }
        protected virtual void OnHandleCreated(EventArgs e) { }
        protected virtual void WndProc(ref F.Message message) { }
        protected virtual void OnFormClosing(F.FormClosingEventArgs e) { }
        protected override void OnClosing(CancelEventArgs e)
        {
            var closing = new F.FormClosingEventArgs(F.CloseReason.UserClosing, e.Cancel);
            OnFormClosing(closing); e.Cancel = closing.Cancel; base.OnClosing(e);
        }
        protected override void OnClosed(EventArgs e)
        {
            Dispose(true); base.OnClosed(e);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (IsDisposed) return;
            IsDisposed = true;
            if (source != null) source.RemoveHook(ProcessMessage);
            handle = IntPtr.Zero;
        }
        public void Dispose()
        {
            if (IsDisposed) return;
            // Dispose the controller first so its Close handler permits closing.
            Dispose(true); Close(); GC.SuppressFinalize(this);
        }
    }
}
