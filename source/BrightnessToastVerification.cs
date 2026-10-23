using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace MonitorShade
{
    internal static class BrightnessToastVerification
    {
        private static T Field<T>(BrightnessToast toast, string name)
        { return (T)typeof(BrightnessToast).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(toast); }

        internal static int Run(string path)
        {
            var lines = new List<string>();
            Action<bool, string> check = (ok, message) => { if (!ok) throw new Exception(message); lines.Add("PASS: " + message); };
            try
            {
                using (var toast = new BrightnessToast())
                {
                    // No desktop capture, cursor movement, or DDC writes. Render the
                    // actual popup to a bitmap at each scale, including mixed-DPI reuse.
                    foreach (int dpi in new[] { 96, 120, 144, 168, 192, 240, 288, 96 })
                    foreach (bool korean in new[] { true, false })
                    foreach (bool narrow in new[] { false, true })
                    {
                        string title = korean ? "모니터 밝기" : "Monitor brightness";
                        string value = korean ? "모니터 1  65%  ·  모니터 2  65%" : "Monitor 1  100%  ·  Monitor 2  100%";
                        if (narrow) value += korean ? "  ·  모니터 3  100%  ·  모니터 4  100%" : "  ·  Monitor 3  100%  ·  Monitor 4  100%";
                        Rectangle area = new Rectangle(-2160, -628, narrow ? 600 : 3840, 2160);
                        toast.Arrange(title, value, dpi, area);
                        // Force hidden HWND creation before the final arrangement,
                        // just as Display does before reading the monitor DPI.
                        IntPtr handle = toast.Handle;
                        toast.Arrange(title, value, dpi, area);
                        check(area.Contains(toast.Bounds), "Toast fits target work area: " + dpi + "/" + korean + "/" + narrow);
                        using (var actual = new Bitmap(toast.Width, toast.Height))
                        using (var reference = new Bitmap(toast.Width, toast.Height + 256))
                        {
                            toast.DrawToBitmap(actual, new Rectangle(Point.Empty, actual.Size));
                            Rectangle titleBox = Field<Rectangle>(toast, "headingBounds"), detailBox = Field<Rectangle>(toast, "valueBounds");
                            // Render with unlimited vertical space. A clipped title or
                            // descender differs from this reference even if layout
                            // rectangles themselves appear to fit inside the window.
                            using (var g = Graphics.FromImage(reference))
                            {
                                g.Clear(toast.BackColor);
                                var flags = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
                                TextRenderer.DrawText(g, title, Field<Font>(toast, "headingFont"), new Rectangle(titleBox.X, titleBox.Y, titleBox.Width, reference.Height - titleBox.Y), Color.White, toast.BackColor, flags);
                                TextRenderer.DrawText(g, value, Field<Font>(toast, "valueFont"), new Rectangle(detailBox.X, detailBox.Y, detailBox.Width, reference.Height - detailBox.Y), Color.FromArgb(96, 205, 255), toast.BackColor, flags);
                            }
                            int differences = 0, ink = 0;
                            for (int y = 0; y < reference.Height; y++)
                            for (int x = 0; x < reference.Width; x++)
                            {
                                int expected = reference.GetPixel(x, y).ToArgb();
                                if (expected != toast.BackColor.ToArgb()) ink++;
                                int observed = y < actual.Height ? actual.GetPixel(x, y).ToArgb() : toast.BackColor.ToArgb();
                                if (expected != observed) differences++;
                            }
                            check(ink > 100 && differences == 0, "All glyph pixels survive without clipping: " + dpi + "/" + korean + "/" + narrow);
                            if (korean && !narrow && (dpi == 96 || dpi == 144 || dpi == 192))
                                actual.Save(path + "." + dpi + ".png", ImageFormat.Png);
                        }
                    }
                    check(!toast.Visible, "Verification leaves no popup on the desktop");
                }
                File.WriteAllLines(path, lines); return 0;
            }
            catch (Exception e) { lines.Add("FAIL: " + e); File.WriteAllLines(path, lines); return 1; }
        }
    }
}
