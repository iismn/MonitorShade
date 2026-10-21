using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using F = System.Windows.Forms;

namespace MonitorShade
{
    // Native WPF surface: an ElementHost child HWND would obscure the DWM backdrop.
    // Keep the controller's small WinForms lifetime/message interface while using one WPF HWND.
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
