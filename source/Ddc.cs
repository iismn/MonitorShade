using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace MonitorShade
{
    public sealed class BrightnessPreferences
    {
        public bool HotkeysEnabled = true;
        public string Modifier = "RightAlt";
        public int DownKey = 0x71, UpKey = 0x72, Step = 5;
        public string Target = "Selected";
        public bool SyncEnabled = true;
        public MonitorBrightnessPreference[] Monitors = new MonitorBrightnessPreference[0];
        public void Normalize()
        {
            if (!new[] { "RightAlt", "LeftAlt", "CtrlAlt", "CtrlShift" }.Contains(Modifier)) Modifier = "RightAlt";
            if (!BrightnessBinding.Valid(Modifier, DownKey, UpKey)) { DownKey = 0x71; UpKey = 0x72; }
            if (Step < 1 || Step > 20) Step = 5;
            if (Target != "Selected" && Target != "Pointer") Target = "Selected";
            Monitors = (Monitors ?? new MonitorBrightnessPreference[0]).Where(m => m != null && !string.IsNullOrEmpty(m.Id)).GroupBy(m => m.Id).Select(g => g.First()).ToArray();
        }
    }
    public sealed class MonitorBrightnessPreference { public string Id = ""; public bool Selected = true; }
    public sealed class DdcMonitor
    {
        public string Id = "", Device = "", Name = "", Status = "", Capabilities = "";
        public int X, Y, Width, Height, PhysicalIndex;
        public bool Primary, Supported, Verified;
        public uint Current, Maximum;
        public float PeakNits, FullFrameNits;
        public bool Hdr;
        [XmlIgnore] public int Percent { get { return DdcMath.Percent(Current, Maximum); } }
        [XmlIgnore] public Rectangle Bounds { get { return new Rectangle(X, Y, Width, Height); } }
    }
    public sealed class DdcRequest
    {
        public string Action = "scan";
        public DdcChange[] Changes = new DdcChange[0];
        public bool Capabilities;
    }
    public sealed class DdcChange { public string Id = ""; public int Percent; }
    public sealed class DdcResponse { public DdcMonitor[] Monitors = new DdcMonitor[0]; public string Error = ""; }
    internal static class DdcMath
    {
        internal static int Percent(uint current, uint maximum) { return maximum == 0 ? 0 : (int)Math.Round(Math.Min(current, maximum) * 100.0 / maximum); }
        internal static uint Raw(int percent, uint maximum) { return (uint)Math.Round(Math.Max(0, Math.Min(100, percent)) * (double)maximum / 100); }
        internal static bool Valid(uint current, uint maximum) { return maximum > 0 && maximum <= 65535 && current <= maximum; }
    }

    internal static class DdcWorker
    {
        internal static int Run(string requestPath, string responsePath)
        {
            DdcResponse response;
            try { using (var stream = File.OpenRead(requestPath)) response = Execute((DdcRequest)new XmlSerializer(typeof(DdcRequest)).Deserialize(stream)); }
            catch (Exception e) { response = new DdcResponse { Error = e.Message }; }
            using (var stream = File.Create(responsePath)) new XmlSerializer(typeof(DdcResponse)).Serialize(stream, response);
            return string.IsNullOrEmpty(response.Error) ? 0 : 1;
        }
        internal static DdcResponse Execute(DdcRequest request)
        {
            if (request.Action != "scan" && request.Action != "set") throw new ArgumentException("Unknown DDC operation");
            if (request.Action == "set" && (request.Changes == null || request.Changes.Length == 0 || request.Changes.Any(c => c == null || string.IsNullOrEmpty(c.Id) || c.Percent < 0 || c.Percent > 100))) throw new ArgumentException("Invalid brightness request");
            List<DdcMonitor> result = new List<DdcMonitor>();
            var luminance = DxgiLuminance.Read();
            DdcNative.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr monitor, IntPtr dc, ref DdcNative.RECT bounds, IntPtr context)
            {
                var info = new DdcNative.MONITORINFO { Size = Marshal.SizeOf(typeof(DdcNative.MONITORINFO)) };
                if (!DdcNative.GetMonitorInfo(monitor, ref info)) return true;
                var device = new DdcNative.DISPLAY_DEVICE { Size = Marshal.SizeOf(typeof(DdcNative.DISPLAY_DEVICE)) };
                string identity = DdcNative.EnumDisplayDevices(info.Device, 0, ref device, 1) ? device.Id : "";
                // Refuse brightness writes when a stable physical identity cannot be obtained.
                uint count;
                if (!DdcNative.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out count) || count == 0 || count > 16)
                {
                    result.Add(new DdcMonitor { Device = info.Device, Name = device.Name ?? info.Device, Status = "DDC/CI 장치에 접근할 수 없습니다.", X = bounds.Left, Y = bounds.Top, Width = bounds.Right - bounds.Left, Height = bounds.Bottom - bounds.Top }); return true;
                }
                var physical = new DdcNative.PHYSICAL_MONITOR[count];
                if (!DdcNative.GetPhysicalMonitorsFromHMONITOR(monitor, count, physical)) return true;
                try
                {
                    for (int index = 0; index < count; index++)
                    {
                        var item = new DdcMonitor { Id = string.IsNullOrEmpty(identity) ? "" : identity + "|" + index, Device = info.Device, Name = MonitorName(identity, physical[index].Description), Primary = (info.Flags & 1) != 0, X = bounds.Left, Y = bounds.Top, Width = bounds.Right - bounds.Left, Height = bounds.Bottom - bounds.Top, PhysicalIndex = index };
                        DdcChange change = request.Action == "set" ? request.Changes.FirstOrDefault(c => c.Id == item.Id) : null;
                        if (request.Action == "set" && change == null) continue;
                        DxgiLuminance.Value value;
                        if (luminance.TryGetValue(info.Device, out value)) { item.PeakNits = value.Peak; item.FullFrameNits = value.FullFrame; item.Hdr = value.Hdr; }
                        uint type, current, maximum;
                        bool read = DdcNative.GetVCPFeatureAndVCPFeatureReply(physical[index].Handle, 0x10, out type, out current, out maximum);
                        if (!read) { Thread.Sleep(100); read = DdcNative.GetVCPFeatureAndVCPFeatureReply(physical[index].Handle, 0x10, out type, out current, out maximum); }
                        item.Supported = read && DdcMath.Valid(current, maximum) && !string.IsNullOrEmpty(item.Id);
                        item.Current = current; item.Maximum = maximum;
                        item.Status = item.Supported ? "DDC/CI 밝기 읽기 성공" : "밝기 응답 없음 · 모니터 OSD의 DDC/CI, HDR 모드를 확인하세요.";
                        if (change != null)
                        {
                            if (!item.Supported) item.Status = "읽기를 확인하지 못해 밝기를 변경하지 않았습니다.";
                            else
                            {
                                uint desired = DdcMath.Raw(change.Percent, maximum);
                                if (desired == current) { item.Verified = true; item.Status = "현재 밝기와 같습니다."; }
                                // Brightness (0x10) is the only writable VCP code in this program.
                                else if (!DdcNative.SetVCPFeature(physical[index].Handle, 0x10, desired)) item.Status = "밝기 쓰기 실패 · 모니터 상태를 확인하세요.";
                                else
                                {
                                    bool reread = false;
                                    for (int retry = 0; retry < 3; retry++)
                                    {
                                        Thread.Sleep(120);
                                        if (DdcNative.GetVCPFeatureAndVCPFeatureReply(physical[index].Handle, 0x10, out type, out current, out maximum) && DdcMath.Valid(current, maximum))
                                        { reread = true; item.Current = current; item.Maximum = maximum; if (current == desired) { item.Verified = true; break; } }
                                    }
                                    item.Status = item.Verified ? "밝기 적용 및 읽기 확인 완료" : reread ? "요청한 값이 적용되지 않았습니다 · HDR/화면 모드 확인" : "쓰기 전송 후 읽기 확인 실패 · 새로고침하세요.";
                                }
                            }
                        }
                        if (request.Capabilities)
                        {
                            uint length;
                            if (DdcNative.GetCapabilitiesStringLength(physical[index].Handle, out length) && length > 0 && length <= 16384)
                            {
                                var caps = new StringBuilder((int)length);
                                if (DdcNative.CapabilitiesRequestAndCapabilitiesReply(physical[index].Handle, caps, length)) { item.Capabilities = caps.ToString(); var model = System.Text.RegularExpressions.Regex.Match(item.Capabilities, @"model\(([^)]+)\)"); if (model.Success) item.Name = model.Groups[1].Value; }
                            }
                        }
                        result.Add(item);
                    }
                }
                finally { DdcNative.DestroyPhysicalMonitors(count, physical); }
                return true;
            }, IntPtr.Zero);
            if (request.Action == "set")
                foreach (var change in request.Changes) if (!result.Any(m => m.Id == change.Id)) result.Add(new DdcMonitor { Id = change.Id, Status = "대상 모니터 연결이 바뀌었습니다. 새로고침하세요." });
            return new DdcResponse { Monitors = result.OrderByDescending(m => m.Primary).ThenBy(m => m.X).ToArray(), Error = result.Count == 0 ? "연결된 모니터를 찾지 못했습니다." : "" };
        }
        private static string MonitorName(string identity, string fallback)
        {
            try
            {
                string[] parts = identity.Split('#');
                if (parts.Length >= 3)
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + parts[1] + "\\" + parts[2] + @"\Device Parameters"))
                    {
                        byte[] edid = key == null ? null : key.GetValue("EDID") as byte[];
                        if (edid != null && edid.Length >= 128)
                            for (int offset = 54; offset <= 108; offset += 18)
                                if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 3] == 0xFC) return Encoding.ASCII.GetString(edid, offset + 5, 13).Trim('\0', ' ', '\n', '\r');
                    }
            }
            catch { }
            return string.IsNullOrWhiteSpace(fallback) ? "외부 모니터" : fallback;
        }
    }

    // A driver timeout cannot block the UI or the keyboard hook. Every request owns a bounded child process.
    internal sealed class DdcClient : IDisposable
    {
        private readonly object sync = new object();
        private Process active;
        private bool disposed;
        internal Task<DdcResponse> Send(DdcRequest request)
        {
            return Task.Run(delegate
            {
                lock (sync)
                {
                    if (disposed) return new DdcResponse { Error = "종료됨" };
                    string folder = Path.Combine(Path.GetTempPath(), "MonitorShade-ddc-" + Guid.NewGuid().ToString("N"));
                    string input = Path.Combine(folder, "request.xml"), output = Path.Combine(folder, "response.xml");
                    try
                    {
                        Directory.CreateDirectory(folder);
                        using (var stream = File.Create(input)) new XmlSerializer(typeof(DdcRequest)).Serialize(stream, request);
                        using (var process = new Process { StartInfo = new ProcessStartInfo { FileName = Application.ExecutablePath, Arguments = "--ddc-worker \"" + input + "\" \"" + output + "\"", UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden } })
                        {
                            active = process; process.Start();
                            if (!process.WaitForExit(request.Capabilities ? 25000 : 12000)) { try { process.Kill(); process.WaitForExit(1000); } catch { } return new DdcResponse { Error = "모니터 응답 시간이 초과됐습니다. DDC/CI와 연결 상태를 확인하세요." }; }
                            if (!File.Exists(output)) return new DdcResponse { Error = "모니터 통신 결과를 읽지 못했습니다." };
                            using (var stream = File.OpenRead(output)) return (DdcResponse)new XmlSerializer(typeof(DdcResponse)).Deserialize(stream);
                        }
                    }
                    catch (Exception e) { return new DdcResponse { Error = e.Message }; }
                    finally { active = null; try { Directory.Delete(folder, true); } catch { } }
                }
            });
        }
        public void Dispose() { disposed = true; Process process = active; if (process != null) try { if (!process.HasExited) process.Kill(); } catch { } }
    }

    internal static class DdcNative
    {
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct MONITORINFO { internal int Size; internal RECT Monitor, Work; internal uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Device; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct DISPLAY_DEVICE { internal int Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Device; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Name; internal uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Id; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Key; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct PHYSICAL_MONITOR { internal IntPtr Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Description; }
        internal delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref RECT bounds, IntPtr context);
        [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool EnumDisplayDevices(string device, uint index, ref DISPLAY_DEVICE output, uint flags);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PHYSICAL_MONITOR[] physical);
        [DllImport("dxva2.dll")] internal static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] physical);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, out uint type, out uint current, out uint maximum);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);
        [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetCapabilitiesStringLength(IntPtr monitor, out uint length);
        [DllImport("dxva2.dll", CharSet = CharSet.Ansi, SetLastError = true)] internal static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr monitor, StringBuilder value, uint length);
    }

    internal static class DxgiLuminance
    {
        internal sealed class Value { internal float Peak, FullFrame; internal bool Hdr; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DESC
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Device;
            internal DdcNative.RECT Bounds; internal int Attached, Rotation; internal IntPtr Monitor; internal uint Bits, ColorSpace;
            internal float Rx, Ry, Gx, Gy, Bx, By, Wx, Wy, Minimum, Maximum, FullFrame;
        }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumItem(IntPtr self, uint index, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int DescCall(IntPtr self, out DESC description);
        [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid id, out IntPtr factory);
        private static T Method<T>(IntPtr instance, int slot) where T : class { return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size), typeof(T)) as T; }
        internal static Dictionary<string, Value> Read()
        {
            var result = new Dictionary<string, Value>(StringComparer.OrdinalIgnoreCase);
            IntPtr factory = IntPtr.Zero;
            try
            {
                Guid iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
                if (CreateDXGIFactory1(ref iid, out factory) < 0) return result;
                for (uint a = 0; a < 16; a++)
                {
                    IntPtr adapter; if (Method<EnumItem>(factory, 12)(factory, a, out adapter) < 0) break;
                    try
                    {
                        for (uint o = 0; o < 32; o++)
                        {
                            IntPtr output; if (Method<EnumItem>(adapter, 7)(adapter, o, out output) < 0) break;
                            IntPtr output6 = IntPtr.Zero;
                            try
                            {
                                Guid outputId = new Guid("068346e8-aaec-4b84-add7-137f513f77a1");
                                if (Marshal.QueryInterface(output, ref outputId, out output6) < 0) continue;
                                DESC desc;
                                if (Method<DescCall>(output6, 27)(output6, out desc) >= 0 && desc.Attached != 0)
                                    result[desc.Device] = new Value { Peak = Valid(desc.Maximum), FullFrame = Valid(desc.FullFrame), Hdr = desc.ColorSpace == 12 };
                            }
                            finally { if (output6 != IntPtr.Zero) Marshal.Release(output6); Marshal.Release(output); }
                        }
                    }
                    finally { Marshal.Release(adapter); }
                }
            }
            catch { }
            finally { if (factory != IntPtr.Zero) Marshal.Release(factory); }
            return result;
        }
        private static float Valid(float value) { return float.IsNaN(value) || float.IsInfinity(value) || value <= 0 || value > 10000 ? 0 : value; }
    }
}
