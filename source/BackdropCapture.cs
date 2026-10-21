using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using I = System.Windows.Media.Imaging;
using M = System.Windows.Media;

namespace MonitorShade
{
    // Capture only our own magnifier helper. Its per-control filter excludes the
    // settings HWND, so the settings window stays visible to normal screen sharing.
    internal sealed class BackdropCapture : IDisposable
    {
        [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface CaptureInterop
        {
            IntPtr CreateForWindow(IntPtr window, ref Guid iid);
            IntPtr CreateForMonitor(IntPtr monitor, ref Guid iid);
        }
        [ComImport, Guid("905A0FEF-BC53-11DF-8C49-001E4FC686DA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface BufferAccess { void Buffer(out IntPtr pointer); }
        [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(IntPtr adapter, int driver, IntPtr software, uint flags, IntPtr levels, uint count, uint sdk, out IntPtr device, out int level, out IntPtr context);
        [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr device, out IntPtr result);
        [DllImport("combase.dll")] private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string value, int length, out IntPtr result);
        [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr value);
        [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr name, ref Guid iid, out IntPtr result);

        private IDirect3DDevice device;
        private Direct3D11CaptureFramePool pool;
        private GraphicsCaptureSession session;
        private readonly Action<I.BitmapSource> received;
        private readonly Action<string> failed;
        private volatile bool stopped;
        private long lastFrame;
        private int processing;

        internal BackdropCapture(IntPtr window, Action<I.BitmapSource> received, Action<string> failed)
        {
            this.received = received; this.failed = failed;
            IntPtr native = IntPtr.Zero, context = IntPtr.Zero, dxgi = IntPtr.Zero, inspectable = IntPtr.Zero, name = IntPtr.Zero, factory = IntPtr.Zero, itemPointer = IntPtr.Zero;
            try
            {
                int level;
                Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out native, out level, out context));
                var dxgiId = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native, ref dxgiId, out dxgi));
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
                device = (IDirect3DDevice)Marshal.GetObjectForIUnknown(inspectable);
                string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
                Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out name));
                var interopId = typeof(CaptureInterop).GUID;
                Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, ref interopId, out factory));
                var interop = (CaptureInterop)Marshal.GetObjectForIUnknown(factory);
                var itemId = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
                itemPointer = interop.CreateForWindow(window, ref itemId);
                var item = (GraphicsCaptureItem)Marshal.GetObjectForIUnknown(itemPointer);
                pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
                pool.FrameArrived += FrameArrived;
                session = pool.CreateCaptureSession(item);
                session.IsCursorCaptureEnabled = false;
                session.StartCapture();
            }
            catch { Dispose(); throw; }
            finally
            {
                if (name != IntPtr.Zero) WindowsDeleteString(name);
                foreach (IntPtr p in new[] { itemPointer, factory, inspectable, dxgi, context, native }) if (p != IntPtr.Zero) Marshal.Release(p);
            }
        }

        private void FrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            if (stopped || Interlocked.CompareExchange(ref processing, 1, 0) != 0) return;
            try
            {
                using (var frame = sender.TryGetNextFrame())
                {
                    if (frame == null || stopped) return;
                    long now = Stopwatch.GetTimestamp();
                    if ((now - lastFrame) * 1000.0 / Stopwatch.Frequency < 100) return;
                    lastFrame = now;
                    var operation = SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                    try
                    {
                        var timeout = Stopwatch.StartNew();
                        while (operation.Status == Windows.Foundation.AsyncStatus.Started)
                        {
                            if (stopped || timeout.ElapsedMilliseconds > 1000) { operation.Cancel(); return; }
                            Thread.Sleep(2);
                        }
                        using (var bitmap = operation.GetResults())
                        {
                            if (stopped) return;
                            int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
                            if (width < 1 || height < 1 || width > 2048 || height > 2048) throw new InvalidOperationException("배경 크기를 확인할 수 없습니다.");
                            byte[] bytes = new byte[checked(width * height * 4)];
                            var buffer = new Windows.Storage.Streams.Buffer((uint)bytes.Length);
                            bitmap.CopyToBuffer(buffer);
                            IntPtr pointer; ((BufferAccess)(object)buffer).Buffer(out pointer);
                            Marshal.Copy(pointer, bytes, 0, bytes.Length);
                            // Bgr32 declares a fully composed RGB background, not a transparent
                            // bitmap. UI/card alpha is still applied later by the WPF renderer.
                            var source = I.BitmapSource.Create(width, height, 96, 96, M.PixelFormats.Bgr32, null, bytes, width * 4);
                            source.Freeze(); if (!stopped) received(source);
                        }
                    }
                    finally { operation.Close(); }
                }
            }
            catch (Exception e) { if (!stopped) failed(e.Message); }
            finally { Interlocked.Exchange(ref processing, 0); }
        }

        public void Dispose()
        {
            if (stopped) return; stopped = true;
            // Device loss/session shutdown can invalidate COM objects during cleanup.
            // Still release every resource and let the owner use the native fallback.
            if (pool != null) try { pool.FrameArrived -= FrameArrived; } catch (COMException) { }
            if (session != null) { try { session.Dispose(); } catch (COMException) { } session = null; }
            if (pool != null) { try { pool.Dispose(); } catch (COMException) { } pool = null; }
            if (device != null) { try { device.Dispose(); } catch (COMException) { } device = null; }
        }
    }
}
