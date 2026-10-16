using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MonitorShade
{
    // Coordinates stay in physical desktop pixels, including negative monitor origins.
    internal sealed class WindowFootprint
    {
        internal IntPtr Handle;
        internal Rectangle[] Areas;
    }
    internal struct PixelSpan
    {
        internal int Offset, Length;
        internal PixelSpan(int offset, int length) { Offset = offset; Length = length; }
    }
    internal sealed class WallpaperMask
    {
        private readonly Rectangle bounds;
        private readonly Func<Rectangle, WindowFootprint[]> enumerate;
        private WindowFootprint[] previous;
        internal PixelSpan[] Spans { get; private set; }
        internal WallpaperMask(Rectangle area) : this(area, DesktopWindows.Read) { }
        internal WallpaperMask(Rectangle area, Func<Rectangle, WindowFootprint[]> reader)
        { bounds = area; enumerate = reader; }
        internal bool Update()
        {
            // A failed enumeration must never be interpreted as an empty desktop.
            WindowFootprint[] current = enumerate(bounds);
            if (current == null) throw new Win32Exception("앱 창 영역을 확인할 수 없습니다.");
            bool changed = previous == null || !Same(previous, current);
            if (changed) Spans = Build(bounds, current.SelectMany(w => w.Areas));
            previous = current;
            return changed;
        }
        private static bool Same(WindowFootprint[] a, WindowFootprint[] b)
        {
            if (a.Length != b.Length) return false;
            // Preserve Z order: replacing/reordering windows also counts as activity.
            for (int i = 0; i < a.Length; i++)
                if (a[i].Handle != b[i].Handle || !a[i].Areas.SequenceEqual(b[i].Areas)) return false;
            return true;
        }
        internal static PixelSpan[] Build(Rectangle bounds, IEnumerable<Rectangle> windows)
        {
            var spans = new List<PixelSpan>();
            using (var union = new Region())
            using (var matrix = new Matrix())
            {
                union.MakeEmpty();
                foreach (Rectangle window in windows)
                {
                    Rectangle clip = Rectangle.Intersect(bounds, window);
                    if (clip.Width > 0 && clip.Height > 0) union.Union(clip);
                }
                foreach (RectangleF scan in union.GetRegionScans(matrix))
                {
                    int left = (int)Math.Ceiling(scan.Left) - bounds.Left;
                    int right = (int)Math.Floor(scan.Right) - bounds.Left;
                    for (int y = (int)Math.Ceiling(scan.Top) - bounds.Top; y < (int)Math.Floor(scan.Bottom) - bounds.Top; y++)
                        if (right > left) spans.Add(new PixelSpan(checked(y * bounds.Width + left), right - left));
                }
            }
            return spans.ToArray();
        }
        internal static unsafe bool Different(IntPtr a, IntPtr b, PixelSpan[] spans)
        {
            uint* x = (uint*)a.ToPointer(), y = (uint*)b.ToPointer();
            foreach (PixelSpan span in spans)
                for (int i = span.Offset, end = i + span.Length; i < end; i++)
                    if (((x[i] ^ y[i]) & 0x00ffffff) != 0) return true;
            return false;
        }
    }

    internal static class DesktopWindows
    {
        internal static bool IsDesktop(string name)
        {
            // Lively embeds its renderer beneath Progman/WorkerW. Do not exclude
            // whole processes: Lively's settings/preview windows are ordinary apps.
            return name == "Progman" || name == "WorkerW" || name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd";
        }
        internal static WindowFootprint[] Read(Rectangle target)
        {
            var result = new List<WindowFootprint>();
            var excluded = new HashSet<IntPtr>(Application.OpenForms.Cast<Form>().OfType<ShadeWindow>().Where(f => f.IsHandleCreated).Select(f => f.Handle));
            Exception error = null;
            bool success = EnumWindows(delegate(IntPtr window, IntPtr unused)
            {
                try
                {
                    if (excluded.Contains(window) || !IsWindowVisible(window) || IsIconic(window)) return true;
                    int cloaked;
                    if (DwmGetNumber(window, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                    var name = new StringBuilder(256);
                    if (GetClassName(window, name, name.Capacity) == 0)
                    {
                        if (IsWindow(window) && IsWindowVisible(window)) throw new Win32Exception(Marshal.GetLastWin32Error());
                        return true; // Window closed during enumeration.
                    }
                    if (IsDesktop(name.ToString())) return true;
                    Rect native, outer;
                    if (!GetWindowRect(window, out outer)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (DwmGetRect(window, 9, out native, 16) != 0 || native.Right <= native.Left || native.Bottom <= native.Top) native = outer;
                    Rectangle frame = native.Rectangle;
                    if (!frame.IntersectsWith(target)) return true;
                    Rectangle[] areas = Shape(window, frame, outer.Rectangle, target);
                    if (areas.Length > 0) result.Add(new WindowFootprint { Handle = window, Areas = areas });
                    return true;
                }
                catch (Exception e) { error = e; return false; }
            }, IntPtr.Zero);
            if (error != null) throw new Win32Exception("앱 창 영역 확인 대기", error);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "앱 창 목록 확인 대기");
            return result.ToArray();
        }
        private static Rectangle[] Shape(IntPtr window, Rectangle frame, Rectangle outer, Rectangle target)
        {
            using (var region = new Region(frame))
            using (var matrix = new Matrix())
            {
                IntPtr shape = CreateRectRgn(0, 0, 0, 0);
                if (shape == IntPtr.Zero) throw new Win32Exception();
                try
                {
                    int kind = GetWindowRgn(window, shape);
                    if (kind != 0)
                    {
                        using (Region custom = Region.FromHrgn(shape))
                        {
                            custom.Translate(outer.X, outer.Y);
                            region.Intersect(custom);
                        }
                    }
                    else
                    {
                        // DWM rounded corners are not reflected by GetWindowRgn.
                        // Leave tiny corner squares out so wallpaper cannot leak in.
                        int composition, preference;
                        bool framed = DwmGetNumber(window, 1, out composition, 4) == 0 && composition != 0;
                        bool square = DwmGetNumber(window, 33, out preference, 4) == 0 && preference == 1;
                        if (Environment.OSVersion.Version.Build >= 22000 && framed && !square && !IsZoomed(window))
                        {
                            int radius = Math.Min(Math.Min(frame.Width, frame.Height) / 2, (int)Math.Ceiling(9 * GetDpiForWindow(window) / 96.0));
                            if (radius > 0)
                            {
                                region.Exclude(new Rectangle(frame.Left, frame.Top, radius, radius));
                                region.Exclude(new Rectangle(frame.Right - radius, frame.Top, radius, radius));
                                region.Exclude(new Rectangle(frame.Left, frame.Bottom - radius, radius, radius));
                                region.Exclude(new Rectangle(frame.Right - radius, frame.Bottom - radius, radius, radius));
                            }
                        }
                    }
                }
                finally { Native.DeleteObject(shape); }
                region.Intersect(target);
                return region.GetRegionScans(matrix).Select(r => Rectangle.FromLTRB((int)Math.Ceiling(r.Left), (int)Math.Ceiling(r.Top), (int)Math.Floor(r.Right), (int)Math.Floor(r.Bottom))).Where(r => r.Width > 0 && r.Height > 0).ToArray();
            }
        }
        [StructLayout(LayoutKind.Sequential)] private struct Rect
        {
            internal int Left, Top, Right, Bottom;
            internal Rectangle Rectangle { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } }
        }
        private delegate bool EnumCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetRect(IntPtr window, int attribute, out Rect rect, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetNumber(IntPtr window, int attribute, out int value, int size);
        [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    }
}
