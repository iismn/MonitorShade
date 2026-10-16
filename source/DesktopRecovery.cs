using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonitorShade
{
    internal sealed class PointerUnavailableException : Exception
    {
        internal PointerUnavailableException(int code) : base("마우스 위치 확인 대기 (" + code + ")") { }
    }
    internal sealed class CaptureUnavailableException : Exception
    {
        internal CaptureUnavailableException(Exception cause) : base("화면 캡처를 다시 준비하지 못했습니다: " + cause.Message, cause) { }
    }

    internal static class DesktopAccess
    {
        // Observe our own desktop only. Never switch into a lock or UAC desktop.
        internal static bool Available()
        {
            IntPtr desktop = GetThreadDesktop(GetCurrentThreadId());
            int receivesInput, needed;
            if (desktop == IntPtr.Zero || !GetUserObjectInformation(desktop, 6, out receivesInput, 4, out needed) || receivesInput == 0) return false;
            bool screenSaver;
            return SystemParametersInfo(0x0072, 0, out screenSaver, 0) && !screenSaver;
        }
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint thread);
        [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true)]
        private static extern bool GetUserObjectInformation(IntPtr handle, int index, out int value, int length, out int needed);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
    }

    internal interface IWatchSurface : IDisposable
    {
        event Action WakeRequested;
        bool Covered { get; }
        bool CaptureChanged();
        void Cover();
        void Hide();
    }
    internal sealed class WatchSurface : IWatchSurface
    {
        private FrameCapture capture;
        private ShadeWindow shade;
        private WallpaperMask wallpaper;
        public event Action WakeRequested;
        internal WatchSurface(Rectangle bounds) : this(bounds, null) { }
        internal WatchSurface(Rectangle bounds, WallpaperMask mask)
        {
            try
            {
                try
                {
                    bool composition;
                    if (Native.DwmIsCompositionEnabled(out composition) != 0 || !composition) throw new Win32Exception("바탕 화면 합성 대기");
                    capture = new FrameCapture(bounds);
                    wallpaper = mask;
                    if (wallpaper != null) wallpaper.Update();
                    capture.CaptureChanged(wallpaper == null ? null : wallpaper.Spans);
                }
                catch (Win32Exception e) { throw new CaptureUnavailableException(e); }
                shade = new ShadeWindow(bounds);
                shade.WakeRequested += delegate { if (WakeRequested != null) WakeRequested(); };
            }
            catch { Dispose(); throw; }
        }
        public bool Covered { get { return shade != null && shade.Visible; } }
        public bool CaptureChanged()
        {
            try
            {
                bool layoutChanged = wallpaper != null && wallpaper.Update();
                bool pixelsChanged = capture.CaptureChanged(wallpaper == null ? null : wallpaper.Spans);
                return layoutChanged || pixelsChanged;
            }
            catch (Win32Exception e) { throw new CaptureUnavailableException(e); }
        }
        public void Cover() { shade.Cover(); }
        public void Hide() { if (shade != null) shade.Hide(); }
        public void Dispose()
        {
            if (shade != null) { shade.Close(); shade.Dispose(); shade = null; }
            if (capture != null) { capture.Dispose(); capture = null; }
        }
    }

    public sealed class Watcher : IDisposable
    {
        private readonly Timer frames = new Timer { Interval = 250 };
        private readonly Timer mouse = new Timer { Interval = 50 };
        private readonly Func<Point> readPointer;
        private readonly Func<bool> desktopAvailable;
        private readonly Func<Rectangle, IWatchSurface> createSurface;
        private readonly Func<long> now;
        private IWatchSurface surface;
        private IdlePolicy policy;
        private Rectangle bounds;
        private bool running, waiting, suspended;
        private int captureFailures;
        public bool Running { get { return running; } }
        public bool Waiting { get { return running && waiting; } }
        public bool Covered { get { return surface != null && surface.Covered; } }
        internal Rectangle Target { get { return bounds; } }
        public event Action<string> Failed;
        public event Action StateChanged;
        public Watcher() : this(ReadSystemPointer) { }
        internal Watcher(Func<Point> pointerReader) : this(pointerReader, DesktopAccess.Available, delegate(Rectangle area) { return new WatchSurface(area); }, NewClock()) { }
        internal Watcher(Func<Point> pointerReader, bool ignoreWallpaper) : this(pointerReader ?? ReadSystemPointer, DesktopAccess.Available, delegate(Rectangle area) { return new WatchSurface(area, ignoreWallpaper ? new WallpaperMask(area) : null); }, NewClock()) { }
        internal Watcher(Func<Point> pointerReader, Func<bool> desktopReader, Func<Rectangle, IWatchSurface> factory, Func<long> clock)
        {
            readPointer = pointerReader; desktopAvailable = desktopReader; createSurface = factory; now = clock;
            frames.Tick += delegate { PollFrame(); };
            mouse.Tick += delegate { PollMouse(); };
        }
        private static Func<long> NewClock() { var clock = Stopwatch.StartNew(); return delegate { return clock.ElapsedMilliseconds; }; }
        public void Start(Rectangle target, int seconds) { Start(target, seconds, false); }
        internal void Start(Rectangle target, int seconds, bool isSuspended)
        {
            Stop();
            if (target.Width <= 0 || target.Height <= 0) throw new ArgumentException("감시 영역을 확인하세요.");
            policy = new IdlePolicy(seconds * 1000L, now()); bounds = target; suspended = isSuspended; running = true;
            try { PollFrame(true); frames.Start(); }
            catch { Stop(); throw; }
        }
        public void Suspend(bool value)
        {
            suspended = value;
            if (running && value) WaitForDesktop(true);
            // Clearing suspension does not start a stopped/manual-paused watcher.
        }
        private void Wake()
        {
            if (!running) return;
            if (surface != null) surface.Hide();
            policy.Activity(now());
        }
        internal void PollMouse()
        {
            if (!running || waiting || suspended) return;
            try { if (bounds.Contains(readPointer())) Wake(); }
            catch (PointerUnavailableException) { WaitForDesktop(true); }
            catch (Exception e) { Fail(e.Message); }
        }
        private static Point ReadSystemPointer()
        {
            Native.Point point;
            if (!Native.GetCursorPos(out point)) throw new PointerUnavailableException(Marshal.GetLastWin32Error());
            return new Point(point.X, point.Y);
        }
        internal void PollFrame() { PollFrame(false); }
        private void PollFrame(bool starting)
        {
            if (!running) return;
            try
            {
                if (suspended || !desktopAvailable()) { WaitForDesktop(true); return; }
                Point pointer = readPointer();
                if (surface == null)
                {
                    surface = createSurface(bounds);
                    surface.WakeRequested += Wake;
                    policy.Activity(now());
                    captureFailures = 0;
                    bool changed = waiting; waiting = false; frames.Interval = 250; mouse.Start();
                    if (changed && StateChanged != null) StateChanged();
                    return; // A fresh baseline always gets a full idle interval.
                }
                if (surface.CaptureChanged() || bounds.Contains(pointer)) Wake();
                captureFailures = 0;
                if (!surface.Covered && policy.ShouldShade(now(), bounds.Contains(pointer)))
                {
                    if (!desktopAvailable()) { WaitForDesktop(true); return; }
                    surface.Cover();
                }
            }
            catch (PointerUnavailableException) { WaitForDesktop(true); }
            catch (CaptureUnavailableException e)
            {
                // Transitions are quiet; repeated capture failure on an accessible desktop is a real error.
                if (suspended || !desktopAvailable()) { WaitForDesktop(true); return; }
                if (++captureFailures >= 5) { if (starting) throw; Fail(e.Message); }
                else WaitForDesktop(false);
            }
            catch (Exception e) { if (starting) throw; Fail(e.Message); }
        }
        private void WaitForDesktop(bool resetCaptureFailures)
        {
            ReleaseSurface();
            policy.Activity(now());
            if (resetCaptureFailures) captureFailures = 0;
            mouse.Stop(); frames.Interval = 1000;
            bool changed = !waiting; waiting = true;
            if (changed && StateChanged != null) StateChanged();
        }
        private void ReleaseSurface()
        {
            if (surface != null) { surface.WakeRequested -= Wake; surface.Dispose(); surface = null; }
        }
        private void Fail(string reason) { Stop(); if (Failed != null) Failed(reason); }
        public void Stop()
        {
            running = waiting = suspended = false; captureFailures = 0;
            frames.Stop(); mouse.Stop(); ReleaseSurface();
        }
        public void Dispose() { Stop(); frames.Dispose(); mouse.Dispose(); }
    }
}
