// BatteryTray.cs - 外设电量托盘工具 (迈从/ATK 键盘鼠标, 2.4G/蓝牙/有线)
// 编译: build.bat (或 csc /nologo /optimize+ /platform:x64 /target:winexe /win32manifest:src\batterytray.manifest /out:BatteryTray.exe src\BatteryTray.cs /r:System.Windows.Forms.dll /r:System.Drawing.dll)
// 设计: 60秒轮询+插拔刷新; 休眠设备保留上次电量并标注; 左键弹出详情面板; 低电气泡; 电池形图标
// 协议: RealTek A7 feature (K7 Ultra) / Compx G87 (迈从鼠标接收器) / Compx-Nordic52 (ATK) / Nordic54 / BLE系统缓存
using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("BatteryTray")]
[assembly: System.Reflection.AssemblyVersion("1.3.0.0")]

class BatteryTray
{
    // ---------------- 日志 ----------------
    // 写入 %LOCALAPPDATA%\BatteryTray\tray.log, 超过256KB自动清空。只记录变化与异常, 不刷屏。
    static class Log
    {
        static readonly object lk = new object();
        static string path;
        public static void Init()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryTray");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "tray.log");
            }
            catch { path = null; }
        }
        public static void W(string msg)
        {
            if (path == null) return;
            try
            {
                lock (lk)
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 256 * 1024) File.WriteAllText(path, "");
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    // ---------------- Win32 ----------------
    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }
    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage; public ushort UsagePage;
        public ushort InputReportByteLength; public ushort OutputReportByteLength; public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps; public ushort NumberInputValueCaps; public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps; public ushort NumberOutputValueCaps; public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps; public ushort NumberFeatureValueCaps; public ushort NumberFeatureDataIndices;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct OVERLAPPED { public IntPtr Internal; public IntPtr InternalHigh; public uint Offset; public uint OffsetHigh; public IntPtr EventHandle; }
    [StructLayout(LayoutKind.Sequential)]
    struct DEVPROPKEY { public Guid fmtid; public uint pid; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(IntPtr ClassGuid, string Enumerator, IntPtr hwndParent, uint Flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr h, IntPtr devInfo, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA d);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr h, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, uint detailSize, out uint required, ref SP_DEVINFO_DATA di);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInfo(IntPtr h, uint index, ref SP_DEVINFO_DATA di);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInstanceId(IntPtr h, ref SP_DEVINFO_DATA di, StringBuilder sb, uint size, out uint required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceRegistryProperty(IntPtr h, ref SP_DEVINFO_DATA di, uint prop, out uint regType, byte[] buf, uint size, out uint required);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr pp);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll", SetLastError = true)] static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS caps);
    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool HidD_GetProductString(IntPtr h, byte[] buf, int len);
    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool HidD_GetManufacturerString(IntPtr h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetFeature(IntPtr h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(IntPtr h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetOutputReport(IntPtr h, byte[] buf, int len);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFile(string path, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(IntPtr h, byte[] buf, uint len, out uint read, ref OVERLAPPED ov);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(IntPtr h, byte[] buf, uint len, out uint written, ref OVERLAPPED ov);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetOverlappedResult(IntPtr h, ref OVERLAPPED ov, out uint transferred, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CancelIo(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateEvent(IntPtr attr, bool manualReset, bool initial, string name);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(uint pid);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Locate_DevNode(out uint inst, string id, uint flags);
    [DllImport("cfgmgr32.dll")] static extern uint CM_Get_DevNode_Property(uint inst, ref DEVPROPKEY key, out uint type, byte[] buf, ref uint size, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    static readonly Guid HID_GUID = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");
    static readonly DEVPROPKEY BATTERY_KEY = new DEVPROPKEY { fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), pid = 2 };
    const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10, DIGCF_ALLCLASSES = 0x04;
    const int POLL_MS = 60000;          // 轮询间隔
    const int LOW_PCT = 20;             // 低电量阈值
    const int STALE_DROP_MIN = 30;      // 彻底消失(枚举不到)多久后移除; 仍枚举得到的设备只标注休眠,永不移除

    // ---------------- 设备读数 ----------------
    class Reading
    {
        public string Key, Name, Short, Mode, Detail;   // Detail = HID产品串(真实型号, 可能为空)
        public int Pct = -1;
        public int Charging = -1;       // -1未知 0否 1是
        public bool Fresh;
        public DateTime LastOk;
        public bool LowNotified;
    }

    class HidDev
    {
        public string Path, Product, Manufacturer;
        public ushort Vid, Pid, Usage, UsagePage, InLen, OutLen, FeatLen;
        public bool IsVendorPage { get { return UsagePage >= 0xFF00; } }
        public string Key { get { return Vid.ToString("X4") + ":" + Pid.ToString("X4"); } }
    }

    class ProbeResult
    {
        public bool Ok; public int Pct; public int Charging = -1;
        public static ProbeResult Fail = new ProbeResult();
        public static ProbeResult Good(int pct, int charging) { return new ProbeResult { Ok = true, Pct = pct, Charging = charging }; }
    }

    class PollOutcome { public List<Reading> Results = new List<Reading>(); public HashSet<string> Present = new HashSet<string>(); }

    static string GetString(IntPtr h, bool product)
    {
        byte[] buf = new byte[256];
        bool ok = product ? HidD_GetProductString(h, buf, buf.Length) : HidD_GetManufacturerString(h, buf, buf.Length);
        if (!ok) return "";
        return Encoding.Unicode.GetString(buf).TrimEnd('\0');
    }

    static List<HidDev> EnumerateHid()
    {
        var list = new List<HidDev>();
        Guid guid = HID_GUID;
        IntPtr h = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (h == IntPtr.Zero || h == new IntPtr(-1)) return list;
        var did = new SP_DEVICE_INTERFACE_DATA(); did.cbSize = Marshal.SizeOf(did);
        for (uint i = 0; SetupDiEnumDeviceInterfaces(h, IntPtr.Zero, ref guid, i, ref did); i++)
        {
            uint need = 0;
            var di = new SP_DEVINFO_DATA(); di.cbSize = Marshal.SizeOf(di);
            SetupDiGetDeviceInterfaceDetail(h, ref did, IntPtr.Zero, 0, out need, ref di);
            if (need == 0) continue;
            IntPtr buf = Marshal.AllocHGlobal((int)need);
            Marshal.WriteInt32(buf, 0, IntPtr.Size == 8 ? 8 : 6);
            bool ok = SetupDiGetDeviceInterfaceDetail(h, ref did, buf, need, out need, ref di);
            string path = ok ? Marshal.PtrToStringUni(IntPtr.Add(buf, 4)) : null;
            Marshal.FreeHGlobal(buf);
            if (!ok || path == null) continue;
            IntPtr fh = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (fh == IntPtr.Zero || fh == new IntPtr(-1)) continue;
            var dev = new HidDev { Path = path };
            var attr = new HIDD_ATTRIBUTES(); attr.Size = Marshal.SizeOf(attr);
            if (HidD_GetAttributes(fh, ref attr)) { dev.Vid = attr.VendorID; dev.Pid = attr.ProductID; }
            IntPtr pp;
            if (HidD_GetPreparsedData(fh, out pp))
            {
                var caps = new HIDP_CAPS();
                if (HidP_GetCaps(pp, ref caps) == 0x00110000)
                {
                    dev.Usage = caps.Usage; dev.UsagePage = caps.UsagePage;
                    dev.InLen = caps.InputReportByteLength; dev.OutLen = caps.OutputReportByteLength; dev.FeatLen = caps.FeatureReportByteLength;
                }
                HidD_FreePreparsedData(pp);
            }
            dev.Product = GetString(fh, true);
            dev.Manufacturer = GetString(fh, false);
            CloseHandle(fh);
            list.Add(dev);
        }
        SetupDiDestroyDeviceInfoList(h);
        return list;
    }

    static IntPtr OpenRW(string path)
    {
        IntPtr h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (h == IntPtr.Zero || h == new IntPtr(-1)) h = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (h == new IntPtr(-1)) return IntPtr.Zero;
        return h;
    }

    static byte[] ReadInput(IntPtr h, int len, int timeoutMs)
    {
        if (len <= 0) len = 65;
        IntPtr ev = CreateEvent(IntPtr.Zero, true, false, null);
        var ov = new OVERLAPPED(); ov.EventHandle = ev;
        byte[] buf = new byte[len]; uint read = 0;
        bool ok = ReadFile(h, buf, (uint)len, out read, ref ov);
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            if (err != 997) { CloseHandle(ev); return null; }
            if (WaitForSingleObject(ev, (uint)timeoutMs) != 0) { CancelIo(h); WaitForSingleObject(ev, 300); CloseHandle(ev); return null; }
            if (!GetOverlappedResult(h, ref ov, out read, false)) { CloseHandle(ev); return null; }
        }
        CloseHandle(ev);
        if (read == 0) return null;
        var r = new byte[read]; Array.Copy(buf, r, (int)read); return r;
    }

    static bool SendOutput(IntPtr h, byte[] data, int reportLen)
    {
        int n = Math.Max(reportLen, data.Length);
        byte[] buf = new byte[n]; Array.Copy(data, buf, data.Length);
        IntPtr ev = CreateEvent(IntPtr.Zero, true, false, null);
        var ov = new OVERLAPPED(); ov.EventHandle = ev;
        uint w = 0;
        bool ok = WriteFile(h, buf, (uint)n, out w, ref ov);
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            if (err == 997)
            {
                if (WaitForSingleObject(ev, 500) == 0) ok = GetOverlappedResult(h, ref ov, out w, false);
                else { CancelIo(h); WaitForSingleObject(ev, 300); }
            }
        }
        CloseHandle(ev);
        if (ok) return true;
        return HidD_SetOutputReport(h, buf, n);
    }

    static void Drain(IntPtr h, int len) { for (int i = 0; i < 8; i++) { if (ReadInput(h, len, 20) == null) break; } }

    // ---------------- 协议 (只发电量查询帧) ----------------
    // RealTek A7/K7 Ultra: feature 0x11 [F9 FF..], resp去前2字节XOR FF, dec[11]=电量; 全FF=休眠
    static ProbeResult TryA7(IntPtr h, int featLen)
    {
        int len = Math.Max(featLen, 65);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            byte[] cmd = new byte[len]; cmd[0] = 0x11; cmd[1] = 0xF9;
            for (int i = 2; i < len; i++) cmd[i] = 0xFF;
            if (!HidD_SetFeature(h, cmd, len)) return ProbeResult.Fail;
            Thread.Sleep(150);
            byte[] rsp = new byte[len]; rsp[0] = 0x11;
            if (!HidD_GetFeature(h, rsp, len)) return ProbeResult.Fail;
            bool allFF = true;
            for (int i = 2; i < len; i++) if (rsp[i] != 0xFF) { allFF = false; break; }
            int pct = rsp[13] ^ 0xFF;                    // dec[11] = rsp[13]^FF
            if (allFF || pct == 0) { Thread.Sleep(300); continue; } // 休眠/未就绪, 重试一次
            if (pct <= 100) return ProbeResult.Good(pct, -1);
            return ProbeResult.Fail;
        }
        return ProbeResult.Fail;
    }

    // Compx G87 (迈从鼠标接收器/G87键盘): FF02 usage0002, write 13 4A 01, input r[5]=电量
    static ProbeResult TryG87(IntPtr h, int inLen, int outLen)
    {
        Drain(h, inLen);
        byte[] cmd = new byte[20]; cmd[0] = 0x13; cmd[1] = 0x4A; cmd[2] = 0x01;
        if (!SendOutput(h, cmd, outLen > 0 ? outLen : 20)) return ProbeResult.Fail;
        var deadline = DateTime.UtcNow.AddMilliseconds(1200);
        while (DateTime.UtcNow < deadline)
        {
            byte[] r = ReadInput(h, inLen > 0 ? inLen : 65, 200);
            if (r == null) continue;
            if (r.Length >= 6 && r[0] == 0x13 && r[1] == 0x4A && r[5] <= 100)
                return ProbeResult.Good(r[5], -1);
        }
        return ProbeResult.Fail;
    }

    // Compx/Nordic52 (ATK/VXE/VGN): 17B write 08 04 .. 49; resp[6]=电量 resp[7]=有线/充电; 校验和0x55
    static ProbeResult TryN52(IntPtr h, int inLen, int outLen)
    {
        byte[] cmd = new byte[17]; cmd[0] = 0x08; cmd[1] = 0x04; cmd[16] = 0x49;
        if (!SendOutput(h, cmd, outLen > 0 ? outLen : 17)) return ProbeResult.Fail;
        Thread.Sleep(120);
        for (int i = 0; i < 6; i++)
        {
            byte[] r = ReadInput(h, inLen > 0 ? inLen : 17, 250);
            if (r == null) continue;
            if (r.Length >= 8 && r[0] == 0x08 && r[1] == 0x04 && r[6] <= 100)
            {
                int sum = 0; foreach (byte b in r) sum += b;
                if ((sum & 0xFF) != 0x55) continue;
                return ProbeResult.Good(r[6], r[7] != 0 ? 1 : 0);
            }
        }
        return ProbeResult.Fail;
    }

    // Nordic54 (ATK Zero): 64B write 08 7D 72 02 00 link 07 01; resp[7]=电量
    static ProbeResult TryN54(IntPtr h, int inLen, int outLen)
    {
        byte[] cmd = new byte[64];
        cmd[0] = 0x08; cmd[1] = 0x7D; cmd[2] = 0x72; cmd[3] = 0x02; cmd[5] = 1; cmd[6] = 0x07; cmd[7] = 0x01;
        if (!SendOutput(h, cmd, outLen > 0 ? outLen : 64)) return ProbeResult.Fail;
        Thread.Sleep(120);
        for (int i = 0; i < 6; i++)
        {
            byte[] r = ReadInput(h, inLen > 0 ? inLen : 64, 250);
            if (r == null) continue;
            if (r.Length >= 10 && r[1] == 0x72 && r[5] == 0x07 && r[2] == 0 && r[7] <= 100)
                return ProbeResult.Good(r[7], -1);
        }
        return ProbeResult.Fail;
    }

    // ---------------- 型号表 ----------------
    static Dictionary<uint, string> models;
    static void InitModels()
    {
        models = new Dictionary<uint, string>();
        Action<ushort, ushort, string> add = (v, p, n) => models[(uint)(v << 16 | p)] = n;
        add(0x373B, 0x11FE, "ATK 8K NANO 接收器"); add(0x373B, 0x1031, "ATK F1 Ultimate (2.4G)"); add(0x373B, 0x102E, "ATK F1 Ultimate (有线)");
        add(0x373B, 0x11D9, "ATK A9 Ultimate (2.4G)"); add(0x373B, 0x11B6, "ATK A9 Ultimate (有线)");
        add(0x373B, 0x1155, "ATK Zero 8k dongle-L1"); add(0x373B, 0x124F, "ATK Zero 8k dongle-L0"); add(0x373B, 0x1154, "ATK Zero (有线)");
        add(0x3554, 0xF58A, "VXE R1 Pro Max (2.4G)"); add(0x3554, 0xF58C, "VXE R1 Pro Max (有线)");
        add(0x3554, 0xF58E, "VXE R1 SE+ (2.4G)"); add(0x3554, 0xF58F, "VXE R1 SE+ (有线)");
        add(0x3554, 0xF503, "VGN F1 Pro (2.4G)"); add(0x3554, 0xF502, "VGN F1 Pro (有线)");
        add(0x3554, 0xFB3E, "VGN F2 Pro Max (2.4G)"); add(0x3554, 0xFB3D, "VGN F2 Pro Max (有线)");
        add(0x3837, 0x1001, "迈从 K7 Ultra"); add(0x41E4, 0x2004, "迈从 2.4G 接收器"); add(0x41E4, 0x2203, "迈从 G98 V2");
        add(0x41E4, 0x2001, "迈从 G87");
        add(0x2023, 0xF013, "迈从 A5 系列 (2.4G)"); add(0x2023, 0xF017, "迈从 A5 Pro (有线)"); add(0x2023, 0xF019, "迈从 A5 Pro Max (有线)");
        add(0x2023, 0xF024, "迈从 AX5 系列 (2.4G)"); add(0x2023, 0xF026, "迈从 AX5 Pro (有线)"); add(0x2023, 0xF022, "迈从 AX5 Pro Max (有线)");
        add(0x5253, 0x1021, "迈从 A7 (2.4G)"); add(0x5253, 0x1020, "迈从 L7 Pro (2.4G)"); add(0x5253, 0x00B0, "迈从 L7 Pro (有线)");
        add(0x3837, 0x1016, "迈从 V7 (2.4G)"); add(0x3837, 0x418C, "迈从 G3 V2 (有线)"); add(0x3837, 0x4042, "迈从 G3 V2 Pro (有线)");
        add(0x3837, 0x4012, "迈从 A7e (有线)"); add(0x3837, 0x4013, "迈从 A7e Pro (有线)");
    }
    static string ModelName(ushort vid, ushort pid)
    {
        uint k = (uint)(vid << 16 | pid);
        return models.ContainsKey(k) ? models[k] : null;
    }
    static bool IsWiredPid(ushort vid, ushort pid)
    {
        uint k = (uint)(vid << 16 | pid);
        switch (k)
        {
            case 0x373B102E: case 0x373B11B6: case 0x373B1154:
            case 0x3554F58C: case 0x3554F58F: case 0x3554F502: case 0x3554FB3D:
            case 0x2023F017: case 0x2023F019: case 0x2023F026: case 0x2023F022:
            case 0x525300B0: case 0x3837418C: case 0x38374042: case 0x38374012: case 0x38374013:
                return true;
        }
        return false;
    }
    static string ShortName(ushort vid, ushort pid, string product)
    {
        if (vid == 0x3837 && pid == 0x1001) return "K7";
        if (vid == 0x41E4 && pid == 0x2004) return "接收器";
        if (vid == 0x41E4 && pid == 0x2203) return "G98";
        if (vid == 0x373B) return "ATK";
        if (vid == 0x3554) return "VXE";
        string m = ModelName(vid, pid);
        if (m != null) return m.Length > 8 ? m.Substring(0, 8) : m;
        if (product != null && product.Length > 0) return product.Length > 8 ? product.Substring(0, 8) : product;
        return vid.ToString("X4");
    }

    // ---------------- 自定义设备 (devices.txt) ----------------
    class Candidate { public string VidPid, Name, Proto; public int Pct = -1; public bool Exists; }
    static Dictionary<string, string> customDevs = new Dictionary<string, string>();   // VID:PID -> 名称
    static HashSet<string> ignoredDevs = new HashSet<string>();   // !VID:PID 已删除设备
    static DateTime customMtime;
    static readonly object customLock = new object();
    static string CustomPath()
    {
        string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        return Path.Combine(dir ?? ".", "devices.txt");
    }
    static void LoadCustom()
    {
        try
        {
            string f = CustomPath();
            if (!File.Exists(f)) { lock (customLock) { customDevs = new Dictionary<string, string>(); customMtime = DateTime.MinValue; } return; }
            DateTime mt = File.GetLastWriteTimeUtc(f);
            var map = new Dictionary<string, string>();
            var ign = new HashSet<string>();
            foreach (string raw in File.ReadAllLines(f, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                bool isIgn = line.StartsWith("!");   // !VID:PID = 已删除设备
                if (isIgn) line = line.Substring(1).Trim();
                string head = line.Split(new[] { ' ', '\t' }, 2)[0].Trim().ToUpper();
                int c = head.IndexOf(':');
                if (c <= 0) continue;
                uint vid, pid;
                if (!uint.TryParse(head.Substring(0, c), System.Globalization.NumberStyles.HexNumber, null, out vid)) continue;
                if (!uint.TryParse(head.Substring(c + 1), System.Globalization.NumberStyles.HexNumber, null, out pid)) continue;
                if (vid > 0xFFFF || pid > 0xFFFF) continue;
                int nameStart = line.ToUpper().IndexOf(head) + head.Length;
                string name = nameStart < line.Length ? line.Substring(nameStart).Trim() : head;
                if (name.Length == 0) name = head;
                string key = vid.ToString("X4") + ":" + pid.ToString("X4");
                if (isIgn) ign.Add(key); else map[key] = name;
            }
            lock (customLock)
            {
                if (mt == customMtime) return;   // 已是最新
                customDevs = map; ignoredDevs = ign; customMtime = mt;
            }
        }
        catch { }
    }
    static bool IsIgnored(string key)
    {
        lock (customLock) { return ignoredDevs.Contains(key); }
    }
    // 删除设备: VID:PID 形态持久化到 devices.txt(!行), 其它键(如蓝牙实例路径)仅本次会话隐藏
    static bool IgnoreDevice(string key)
    {
        lock (customLock)
        {
            try
            {
                ignoredDevs.Add(key);
                customDevs.Remove(key);
                if (key.Length == 9 && key[4] == ':') { WriteConfigLocked(); return true; }
            }
            catch { }
        }
        return false;
    }
    static void WriteConfigLocked()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# BatteryTray 设备列表");
        sb.AppendLine("# 每行: VID:PID 名称 (十六进制); 也可手动编辑, 下轮轮询自动生效");
        sb.AppendLine("# !VID:PID = 已删除(不再显示); 删掉该行即可恢复");
        foreach (var kv in customDevs) sb.AppendLine(kv.Key + " " + kv.Value);
        foreach (var ig in ignoredDevs) sb.AppendLine("!" + ig);
        File.WriteAllText(CustomPath(), sb.ToString(), Encoding.UTF8);
        customMtime = File.GetLastWriteTimeUtc(CustomPath());
    }

    static string CustomName(string key)
    {
        lock (customLock) { return customDevs.ContainsKey(key) ? customDevs[key] : null; }
    }
    static void SaveCustom(string vidPid, string name)
    {
        lock (customLock)
        {
            try
            {
                customDevs[vidPid] = name;
                ignoredDevs.Remove(vidPid);   // 重新添加 = 取消忽略
                WriteConfigLocked();
            }
            catch { }
        }
    }
    static void RemoveCustom(string vidPid)
    {
        lock (customLock)
        {
            try
            {
                if (!customDevs.ContainsKey(vidPid)) return;
                customDevs.Remove(vidPid);
                WriteConfigLocked();
            }
            catch { }
        }
    }
    static bool KnownVid(ushort vid)
    {
        return vid == 0x373B || vid == 0x3554 || vid == 0x41E4 || vid == 0x3837 || vid == 0x2023 || vid == 0x5253 || vid == 0xA8A5 || vid == 0xA8A4 || vid == 0x260D || vid == 0x33E4;
    }
    // 全量扫描: 不做VID白名单过滤, 对所有厂商页接口试全部协议 (添加设备向导用)
    static List<Candidate> ScanCandidates()
    {
        LoadCustom();
        var cands = new List<Candidate>();
        var doneKeys = new HashSet<string>();
        foreach (var d in EnumerateHid())
        {
            if (!d.IsVendorPage || doneKeys.Contains(d.Key)) continue;
            doneKeys.Add(d.Key);
            var tries = new List<string>();
            if (d.UsagePage == 0xFF05 && d.OutLen >= 64) tries.Add("N54");
            if (d.UsagePage == 0xFF02 && d.OutLen > 0) tries.Add("N52");
            if (d.InLen > 0 && d.OutLen > 0) tries.Add("G87");
            if (d.FeatLen >= 20) tries.Add("A7");
            if (d.FeatLen == 520) tries.Add("G98");
            if (tries.Count == 0 && d.OutLen > 0 && d.InLen > 0) tries.Add("N52");
            if (tries.Count == 0) continue;
            string custom = CustomName(d.Key);
            string model = ModelName(d.Vid, d.Pid);
            bool knownVid = KnownVid(d.Vid);
            IntPtr h = OpenRW(d.Path);
            if (h == IntPtr.Zero) continue;
            try
            {
                foreach (string pr in tries)
                {
                    ProbeResult r;
                    if (pr == "A7") r = TryA7(h, d.FeatLen);
                    else if (pr == "G87") r = TryG87(h, d.InLen, d.OutLen);
                    else if (pr == "N52") r = TryN52(h, d.InLen, d.OutLen);
                    else if (pr == "G98") r = TryG98(h);
                    else r = TryN54(h, d.InLen, d.OutLen);
                    if (r.Ok)
                    {
                        string nm = custom ?? model ?? ((d.Product ?? "").Length > 0 ? d.Product : d.Key);
                        cands.Add(new Candidate { VidPid = d.Key, Name = nm, Proto = pr, Pct = r.Pct, Exists = custom != null || model != null || knownVid });
                        break;
                    }
                }
            }
            catch { }
            CloseHandle(h);
        }
        return cands;
    }

    // 迈从 G98 V2 (有线): FF02 feature 520B RID 0x09, Set[09 87 00 00 01 00 02 00] 后 Get → [8]=电量, [9]高nibble=充电
    static ProbeResult TryG98(IntPtr h)
    {
        byte[] cmd = new byte[520]; cmd[0] = 0x09; cmd[1] = 0x87; cmd[4] = 0x01; cmd[6] = 0x02;
        if (!HidD_SetFeature(h, cmd, cmd.Length)) return ProbeResult.Fail;
        byte[] rsp = new byte[520]; rsp[0] = 0x09;
        if (!HidD_GetFeature(h, rsp, rsp.Length)) return ProbeResult.Fail;
        if (rsp[1] != 0x87 || rsp[8] > 100 || rsp[8] == 0) return ProbeResult.Fail;
        return ProbeResult.Good(rsp[8], (rsp[9] >> 4) & 0x0F);
    }

    // ---------------- 一轮探测 ----------------
    static PollOutcome Poll()
    {
        var oc = new PollOutcome();
        var results = oc.Results;
        var present = oc.Present;
        var done = new HashSet<string>();
        LoadCustom();
        foreach (var d in EnumerateHid())
        {
            if (!d.IsVendorPage) continue;
            string model = ModelName(d.Vid, d.Pid);
            bool knownVid = KnownVid(d.Vid);
            string custom = CustomName(d.Key);
            if (IsIgnored(d.Key)) continue;   // 用户已删除的设备
            if (!knownVid && model == null && custom == null) continue;
            if (done.Contains(d.Key)) continue;
            bool isRealtek = (d.Manufacturer ?? "").ToLower().Contains("realtek");
            var protos = new List<string>();
            if ((d.Vid == 0x5253 || isRealtek) && d.UsagePage == 0xFF01 && d.FeatLen >= 20) protos.Add("A7");
            if (d.Vid == 0x41E4 && d.UsagePage == 0xFF02 && d.Usage == 0x0002 && d.InLen > 0 && d.OutLen > 0) protos.Add("G87");
            if ((d.Vid == 0x373B || d.Vid == 0x3554 || d.Vid == 0x260D || d.Vid == 0x33E4) && d.UsagePage == 0xFF02 && d.OutLen > 0) protos.Add("N52");
            if (d.UsagePage == 0xFF05 && d.OutLen >= 64) protos.Add("N54");
            if (d.Vid == 0x41E4 && d.UsagePage == 0xFF02 && d.FeatLen == 520) protos.Add("G98"); // 迈从G98 V2有线: 纯feature接口
            if (protos.Count == 0 && (knownVid || custom != null)) { // 未知PID兜底(白名单+devices.txt自定义设备)
                if (d.OutLen > 0) protos.Add("N52");
                if (d.InLen > 0 && d.OutLen > 0) protos.Add("G87");
                if (d.FeatLen >= 20) protos.Add("A7");
                if (d.FeatLen == 520) protos.Add("G98");
            }
            if (protos.Count == 0) continue;
            present.Add(d.Key);   // 设备仍枚举得到: 探测失败只算休眠, 不移除
            IntPtr h = OpenRW(d.Path);
            if (h == IntPtr.Zero) continue;
            try
            {
                foreach (string pr in protos)
                {
                    ProbeResult r;
                    if (pr == "A7") r = TryA7(h, d.FeatLen);
                    else if (pr == "G87") r = TryG87(h, d.InLen, d.OutLen);
                    else if (pr == "N52") r = TryN52(h, d.InLen, d.OutLen);
                    else if (pr == "G98") r = TryG98(h);
                    else r = TryN54(h, d.InLen, d.OutLen);
                    if (r.Ok)
                    {
                        done.Add(d.Key);
                        string dispName = custom ?? model ?? ((d.Manufacturer + " " + d.Product).Trim());
                        string shortNm = custom != null ? (custom.Length > 8 ? custom.Substring(0, 8) : custom) : ShortName(d.Vid, d.Pid, d.Product);
                        results.Add(new Reading {
                            Key = d.Key,
                            Name = dispName,
                            Short = shortNm,
                            Detail = (d.Product ?? "").Trim(),
                            Mode = IsWiredPid(d.Vid, d.Pid) || (d.Vid == 0x41E4 && d.Pid == 0x2203) ? "有线" : "2.4G",
                            Pct = r.Pct, Charging = r.Charging, Fresh = true, LastOk = DateTime.Now
                        });
                        // 未知厂商的新设备读到电量 → 自动记住, 下次免白名单
                        if (custom == null && model == null && !knownVid) SaveCustom(d.Key, shortNm);
                        break;
                    }
                }
            }
            catch { }
            CloseHandle(h);
        }
        // 蓝牙模式: 读系统缓存 DEVPKEY_Bluetooth_Battery (零射频开销)
        try
        {
            foreach (string enumerator in new string[] { "BTHLE", "BTHENUM" })
            {
                IntPtr h = SetupDiGetClassDevs(IntPtr.Zero, enumerator, IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;
                var di = new SP_DEVINFO_DATA(); di.cbSize = Marshal.SizeOf(di);
                for (uint i = 0; SetupDiEnumDeviceInfo(h, i, ref di); i++)
                {
                    var sb = new StringBuilder(512); uint req;
                    string inst = SetupDiGetDeviceInstanceId(h, ref di, sb, 512, out req) ? sb.ToString() : "?";
                    string name = "";
                    byte[] nb = new byte[512]; uint rt;
                    if (SetupDiGetDeviceRegistryProperty(h, ref di, 0xC, out rt, nb, 512, out req)) name = Encoding.Unicode.GetString(nb).TrimEnd('\0');
                    uint inst2;
                    if (CM_Locate_DevNode(out inst2, inst, 0) != 0) continue;
                    var key = BATTERY_KEY; uint type; byte[] buf = new byte[16]; uint size = 16;
                    if (CM_Get_DevNode_Property(inst2, ref key, out type, buf, ref size, 0) != 0 || size < 1) continue;
                    int v = size == 1 ? buf[0] : (size == 2 ? BitConverter.ToUInt16(buf, 0) : (int)BitConverter.ToUInt32(buf, 0));
                    if (v > 100) continue;
                    if (name.Length == 0) name = inst;
                    present.Add("BLE:" + inst);
                    results.Add(new Reading {
                        Key = "BLE:" + inst, Name = name,
                        Short = name.Length > 6 ? name.Substring(0, 6) : name,
                        Mode = "蓝牙", Pct = v, Charging = -1, Fresh = true, LastOk = DateTime.Now
                    });
                }
                SetupDiDestroyDeviceInfoList(h);
            }
        }
        catch { }
        return oc;
    }

    // ---------------- 托盘 UI ----------------
    class PumpForm : Form
    {
        public Action OnDeviceChange;
        const int WM_DEVICECHANGE = 0x0219;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE && OnDeviceChange != null) OnDeviceChange();
            base.WndProc(ref m);
        }
    }

    class TrayApp : ApplicationContext
    {
        NotifyIcon tray;
        PumpForm pump;
        System.Windows.Forms.Timer pollTimer, debounceTimer;
        InfoForm info;
        Dictionary<string, Reading> state = new Dictionary<string, Reading>();
        object gate = new object();
        int polling;
        Dictionary<string, int> lastPct = new Dictionary<string, int>();   // 日志: 上次电量
        HashSet<string> lastFresh = new HashSet<string>();                 // 日志: 上次新鲜集
        Icon lastIcon;

        public TrayApp()
        {
            InitModels();
            pump = new PumpForm();
            pump.ShowInTaskbar = false;
            pump.WindowState = FormWindowState.Minimized;
            pump.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            pump.Opacity = 0;
            pump.OnDeviceChange = () => { debounceTimer.Stop(); debounceTimer.Start(); };
            var h = pump.Handle; // 强制创建窗口以接收消息

            pollTimer = new System.Windows.Forms.Timer(); pollTimer.Interval = POLL_MS;
            pollTimer.Tick += (s, e) => TriggerRefresh();
            debounceTimer = new System.Windows.Forms.Timer(); debounceTimer.Interval = 3000;
            debounceTimer.Tick += (s, e) => { debounceTimer.Stop(); TriggerRefresh(); };

            var menu = new ContextMenuStrip();
            menu.Items.Add("立即刷新", null, (s, e) => TriggerRefresh());
            var autoItem = new ToolStripMenuItem("开机自启动");
            autoItem.Checked = IsAutoStart();
            autoItem.Click += (s, e) => { SetAutoStart(!autoItem.Checked); autoItem.Checked = IsAutoStart(); };
            menu.Items.Add(autoItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => ExitApp());

            tray = new NotifyIcon();
            tray.Icon = MakeIcon(-1, false, false);
            tray.Text = "外设电量";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (info.Visible) { info.Hide(); return; }
                // 刚因点击面板外被 Deactivate 收起 → 本次点击视为"收起", 不再重开
                if ((DateTime.Now - info.LastAutoHide).TotalMilliseconds < 350) return;
                ShowInfo();
            };

            info = new InfoForm();
            info.OnAdd = ShowAddDevice;
            info.OnChanged = TriggerRefresh;
            info.OnDelete = DeleteDevice;
            pollTimer.Start();
            TriggerRefresh();
        }

        void ExitApp()
        {
            Log.W("退出");
            tray.Visible = false;
            tray.Dispose();
            Application.Exit();
        }

        void TriggerRefresh()
        {
            if (Interlocked.CompareExchange(ref polling, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                PollOutcome oc = null;
                try { oc = Poll(); }
                catch (Exception ex) { Log.W("轮询异常: " + ex.Message); }
                finally { Interlocked.Exchange(ref polling, 0); }
                if (oc != null)
                {
                    lock (gate)
                    {
                        var seen = new HashSet<string>();
                        foreach (var r in oc.Results) { state[r.Key] = r; seen.Add(r.Key); }
                        var drop = new List<string>();
                        foreach (var kv in state)
                        {
                            if (seen.Contains(kv.Key)) continue;
                            kv.Value.Fresh = false;
                            // 设备仍在枚举列表(如休眠) → 保留并标注; 真正拔掉才按超时移除
                            if (!oc.Present.Contains(kv.Key) && (DateTime.Now - kv.Value.LastOk).TotalMinutes > STALE_DROP_MIN) drop.Add(kv.Key);
                        }
                        // 日志: 只记录变化(发现/唤醒/休眠/移除/电量变化), 避免每轮刷屏
                        var changes = new List<string>();
                        foreach (var r in oc.Results)
                        {
                            int oldP;
                            bool known = lastPct.TryGetValue(r.Key, out oldP);
                            if (!known) changes.Add("发现 " + r.Name + " " + r.Pct + "%");
                            else if (!lastFresh.Contains(r.Key)) changes.Add("唤醒 " + r.Name + " " + r.Pct + "%");
                            else if (oldP != r.Pct) changes.Add(r.Name + " " + oldP + "%→" + r.Pct + "%");
                            lastPct[r.Key] = r.Pct;
                            lastFresh.Add(r.Key);
                        }
                        foreach (var kv in state)
                        {
                            if (seen.Contains(kv.Key) || drop.Contains(kv.Key) || !lastFresh.Contains(kv.Key)) continue;
                            lastFresh.Remove(kv.Key);
                            if (oc.Present.Contains(kv.Key)) Log.W("休眠 " + kv.Value.Name + " (保留上次电量 " + kv.Value.Pct + "%)");
                        }
                        foreach (string k in drop)
                        {
                            Reading rr;
                            if (state.TryGetValue(k, out rr)) Log.W("移除 " + rr.Name + " (超过" + STALE_DROP_MIN + "分钟未响应)");
                            lastPct.Remove(k); lastFresh.Remove(k);
                            state.Remove(k);
                        }
                        if (changes.Count > 0) Log.W(string.Join("; ", changes.ToArray()));
                    }
                }
                try { pump.BeginInvoke((Action)UpdateUi); } catch { }
            });
        }

        void UpdateUi()
        {
            List<Reading> rs;
            lock (gate) { rs = new List<Reading>(state.Values); }
            // 图标: 取最低电量(新鲜优先); 充电画黄边; 全部休眠灰色
            Reading lowest = null; bool anyFresh = false;
            foreach (var r in rs) if (r.Fresh) { anyFresh = true; if (lowest == null || r.Pct < lowest.Pct) lowest = r; }
            if (lowest == null) foreach (var r in rs) if (lowest == null || r.Pct < lowest.Pct) lowest = r;
            bool stale = !anyFresh;
            int pct = lowest != null ? lowest.Pct : -1;
            bool charging = lowest != null && lowest.Charging == 1;
            var old = lastIcon;
            lastIcon = MakeIcon(pct, charging, stale);
            tray.Icon = lastIcon;
            if (old != null) old.Dispose();
            // 提示文本 (NotifyIcon.Text 上限63字符)
            var sb = new StringBuilder();
            foreach (var r in rs)
            {
                if (sb.Length > 0) sb.Append(" ");
                sb.Append(r.Short).Append(":").Append(r.Pct).Append("%");
                if (!r.Fresh) sb.Append("(休眠)");
            }
            string tip = sb.Length > 0 ? sb.ToString() : "未发现外设";
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            // 低电量提醒
            foreach (var r in rs)
            {
                if (r.Fresh && r.Pct <= LOW_PCT && r.Charging != 1 && !r.LowNotified)
                {
                    r.LowNotified = true;
                    tray.ShowBalloonTip(6000, "外设电量不足", r.Name + " 电量仅 " + r.Pct + "%，请及时充电", ToolTipIcon.Warning);
                    Log.W("低电量提醒: " + r.Name + " " + r.Pct + "%");
                }
                if (r.Pct > LOW_PCT + 5 || r.Charging == 1) r.LowNotified = false;
            }
            if (info.Visible) info.Rebuild(rs);
        }

        void DeleteDevice(string key)
        {
            string nm = key;
            lock (gate)
            {
                Reading rr;
                if (state.TryGetValue(key, out rr)) nm = rr.Name;
                state.Remove(key); lastPct.Remove(key); lastFresh.Remove(key);
            }
            bool persisted = IgnoreDevice(key);
            Log.W("删除设备 " + nm + (persisted ? " (已写入忽略列表)" : " (仅本次会话)"));
            UpdateUi();
        }

        void ShowInfo()
        {
            List<Reading> rs;
            lock (gate) { rs = new List<Reading>(state.Values); }
            info.Rebuild(rs);
            var wa = Screen.PrimaryScreen.WorkingArea;
            info.Location = new Point(wa.Right - info.Width - 12, wa.Bottom - info.Height - 12);
            info.Show();
            ForceForeground(info);
        }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        // 托盘弹出的面板必须借 AttachThreadInput 抢前台焦点, 否则 Win11 静默拒绝 Activate → Deactivate 立即隐藏
        static void ForceForeground(Form f)
        {
            try
            {
                uint cur = GetCurrentThreadId();
                uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
                bool attached = fgThread != 0 && fgThread != cur && AttachThreadInput(cur, fgThread, true);
                f.Activate();
                if (attached) AttachThreadInput(cur, fgThread, false);
            }
            catch { }
        }

        AddDeviceForm addForm;
        void ShowAddDevice()
        {
            if (addForm == null || addForm.IsDisposed) addForm = new AddDeviceForm(ScanCandidates, TriggerRefresh);
            var wa = Screen.PrimaryScreen.WorkingArea;
            int x = wa.Right - addForm.Width - 12, y = wa.Bottom - addForm.Height - 12;
            if (info.Visible) x = info.Left - addForm.Width - 8;   // 面板开着 → 排在它左边
            if (x < wa.Left) x = wa.Left + 8;
            addForm.Location = new Point(x, y);
            addForm.Show();
            ForceForeground(addForm);
        }

        static string ExePath() { return System.Reflection.Assembly.GetExecutingAssembly().Location; }
        static int RunSchtasks(string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", args);
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                var p = System.Diagnostics.Process.Start(psi);
                p.WaitForExit(8000);
                return p.HasExited ? p.ExitCode : -1;
            }
            catch { return -1; }
        }
        static bool IsAutoStart() { return RunSchtasks("/Query /TN BatteryTray") == 0; }
        static void SetAutoStart(bool on)
        {
            int rc = on ? RunSchtasks("/Create /TN BatteryTray /TR \"\\\"" + ExePath() + "\\\"\" /SC ONLOGON /RL HIGHEST /IT /F")
                        : RunSchtasks("/Delete /TN BatteryTray /F");
            Log.W((on ? "开启" : "关闭") + "开机自启 (schtasks rc=" + rc + ")");
        }

        static Icon MakeIcon(int pct, bool charging, bool stale)
        {
            Color ink = stale ? Color.FromArgb(130, 130, 130)
                : pct < 0 ? Color.FromArgb(110, 110, 110)
                : pct <= LOW_PCT ? Color.FromArgb(230, 60, 60)
                : pct <= 50 ? Color.FromArgb(235, 170, 30)
                : Color.FromArgb(40, 180, 90);
            using (var big = new Bitmap(48, 48))
            using (var g = Graphics.FromImage(big))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = new GraphicsPath())
                {
                    path.AddArc(4, 14, 12, 12, 180, 90);
                    path.AddArc(26, 14, 12, 12, 270, 90);
                    path.AddArc(26, 22, 12, 12, 0, 90);
                    path.AddArc(4, 22, 12, 12, 90, 90);
                    path.CloseFigure();
                    using (var dark = new SolidBrush(Color.FromArgb(50, 50, 50))) g.FillPath(dark, path);
                    using (var p = new Pen(ink, 4f)) g.DrawPath(p, path);
                }
                using (var cap = new SolidBrush(ink)) g.FillRectangle(cap, 41, 19, 5, 10);
                if (pct > 0)
                {
                    int w = (int)Math.Round(26 * pct / 100.0);
                    if (w < 4) w = 4;
                    using (var fb = new SolidBrush(ink)) g.FillRectangle(fb, 8, 18, w, 12);
                }
                if (charging && !stale)
                {
                    Point[] bolt = new Point[] {
                        new Point(25, 15), new Point(17, 26), new Point(22, 26),
                        new Point(20, 33), new Point(29, 22), new Point(24, 22), new Point(27, 15) };
                    using (var sb = new SolidBrush(Color.FromArgb(255, 235, 90))) g.FillPolygon(sb, bolt);
                }
                using (var small = new Bitmap(16, 16))
                using (var g2 = Graphics.FromImage(small))
                {
                    g2.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g2.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g2.DrawImage(big, new Rectangle(0, 0, 16, 16), new Rectangle(0, 0, 48, 48), GraphicsUnit.Pixel);
                    IntPtr hi = small.GetHicon();
                    Icon ic = Icon.FromHandle(hi);
                    Icon clone = (Icon)ic.Clone();
                    DestroyIcon(hi);
                    ic.Dispose();
                    return clone;
                }
            }
        }
    }

    class InfoForm : Form
    {
        public DateTime LastAutoHide = DateTime.MinValue;
        public Action OnAdd;      // 右上角"添加设备"
        public Action OnChanged;  // 重命名后触发刷新
        public Action<string> OnDelete; // 行内✕删除设备
        public InfoForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(32, 32, 32);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9f);
            Width = 352;
            Deactivate += (s, e) => { LastAutoHide = DateTime.Now; Hide(); };
        }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW: 不进 Alt-Tab
                return cp;
            }
        }
        public void Rebuild(List<Reading> rs)
        {
            var olds = new List<Control>();
            foreach (Control c in Controls) olds.Add(c);
            Controls.Clear();
            foreach (Control c in olds) try { c.Dispose(); } catch { }
            int y = 10;
            Controls.Add(new Label { Text = "外设电量", Location = new Point(14, y + 3), AutoSize = true, ForeColor = Color.FromArgb(150, 150, 150), Font = new Font("Microsoft YaHei UI", 8f) });
            var addBtn = new Button { Text = "＋ 添加", Bounds = new Rectangle(Width - 78, y - 1, 64, 23), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.FromArgb(120, 200, 255), Font = new Font("Microsoft YaHei UI", 8f), TabStop = false };
            addBtn.FlatAppearance.BorderSize = 0;
            addBtn.Click += (s, e) => { if (OnAdd != null) OnAdd(); };
            Controls.Add(addBtn);
            y += 26;
            if (rs.Count == 0)
            {
                Controls.Add(new Label { Text = "未发现支持的设备", Location = new Point(14, y), AutoSize = true });
                y += 28;
            }
            foreach (var r in rs)
            {
                Color c = !r.Fresh ? Color.FromArgb(140, 140, 140) : r.Pct <= 20 ? Color.FromArgb(255, 90, 90) : r.Pct <= 50 ? Color.FromArgb(255, 190, 60) : Color.FromArgb(90, 220, 120);
                string status = r.Fresh ? (r.Charging == 1 ? "充电中" : "电量正常") : "休眠中 (显示上次电量)";
                int rowY = y;   // 供✎按钮定位重命名框
                Controls.Add(new Label { Text = r.Name, Location = new Point(14, y), AutoSize = true, MaximumSize = new Size(200, 0) });
                Controls.Add(new Label { Text = r.Pct + "%", Location = new Point(224, y - 2), AutoSize = true, ForeColor = c, Font = new Font("Segoe UI", 10f, FontStyle.Bold) });
                Controls.Add(new BarPanel { Pct = r.Pct, Fresh = r.Fresh, Bounds = new Rectangle(266, y + 3, 30, 8) });
                var rn = new Button { Text = "✎", Bounds = new Rectangle(302, y - 2, 20, 20), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.FromArgb(170, 170, 170), Font = new Font("Segoe UI Symbol", 8f), TabStop = false };
                rn.FlatAppearance.BorderSize = 0;
                rn.Click += (s2, e2) =>
                {
                    string k = r.Key, nm = r.Name;
                    using (var rf = new RenameForm(k, nm))
                    {
                        // 贴着面板中该行弹出, 不落在屏幕左上角
                        var wa = Screen.PrimaryScreen.WorkingArea;
                        int px = Left + (Width - rf.Width) / 2;
                        int py = Top + rowY + 10;
                        if (px < wa.Left + 8) px = wa.Left + 8;
                        if (px + rf.Width > wa.Right - 8) px = wa.Right - rf.Width - 8;
                        if (py + rf.Height > wa.Bottom - 8) py = wa.Bottom - rf.Height - 8;
                        if (py < wa.Top + 8) py = wa.Top + 8;
                        rf.Location = new Point(px, py);
                        rf.ShowDialog(this);
                        if (rf.Saved && OnChanged != null) OnChanged();
                    }
                };
                Controls.Add(rn);
                var del = new Button { Text = "✕", Bounds = new Rectangle(324, y - 2, 20, 20), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.FromArgb(230, 120, 120), Font = new Font("Segoe UI", 8f), TabStop = false };
                del.FlatAppearance.BorderSize = 0;
                string dKey = r.Key, dName = r.Name;
                del.Click += (s2, e2) =>
                {
                    if (MessageBox.Show(this, "不再显示「" + dName + "」的电量?\n\n删除后可用「＋ 添加」向导重新扫描恢复。", "删除设备", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes && OnDelete != null) OnDelete(dKey);
                };
                Controls.Add(del);
                y += 22;
                string det = r.Detail != null ? (r.Detail.Length > 24 ? r.Detail.Substring(0, 24) : r.Detail) : "";
                Controls.Add(new Label { Text = (det.Length > 0 ? det + " · " : "") + r.Mode + " · " + status + " · " + r.LastOk.ToString("HH:mm:ss"), Location = new Point(14, y), AutoSize = true, ForeColor = Color.FromArgb(150, 150, 150), Font = new Font("Microsoft YaHei UI", 7.5f) });
                y += 26;
            }
            Controls.Add(new Label { Text = "左键托盘图标 开/关此面板 · 右键: 刷新/自启/退出", Location = new Point(14, y), AutoSize = true, ForeColor = Color.FromArgb(110, 110, 110), Font = new Font("Microsoft YaHei UI", 7.5f) });
            y += 26;
            Height = y + 4;
            SetRounded();
        }
        void SetRounded()
        {
            using (var p = new GraphicsPath())
            {
                int r = 10;
                p.AddArc(0, 0, r, r, 180, 90);
                p.AddArc(Width - r - 1, 0, r, r, 270, 90);
                p.AddArc(Width - r - 1, Height - r - 1, r, r, 0, 90);
                p.AddArc(0, Height - r - 1, r, r, 90, 90);
                p.CloseFigure();
                Region = new Region(p);
            }
        }
    }

    // 添加设备向导: 扫描全部厂商页接口 → 选中新设备存入 devices.txt
    class AddDeviceForm : Form
    {
        Func<List<Candidate>> scanner;
        public Action OnAdded;
        ListBox list;
        TextBox nameBox;
        Button scanBtn, addBtn;
        Label status;
        List<Candidate> cands = new List<Candidate>();
        int scanning;

        public AddDeviceForm(Func<List<Candidate>> scan, Action onAdded)
        {
            scanner = scan; OnAdded = onAdded;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(32, 32, 32);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9f);
            Width = 372; Height = 316;
            Controls.Add(new Label { Text = "添加设备", Location = new Point(14, 10), AutoSize = true, Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold) });
            var closeBtn = new Button { Text = "✕", Bounds = new Rectangle(Width - 36, 9, 24, 22), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.FromArgb(180, 180, 180), TabStop = false };
            closeBtn.FlatAppearance.BorderSize = 0;
            closeBtn.Click += (s, e) => Close();
            Controls.Add(closeBtn);
            Controls.Add(new Label { Text = "动一动设备(唤醒)再扫描; 蓝牙设备配对后自动显示, 无需添加", Location = new Point(14, 40), MaximumSize = new Size(344, 0), AutoSize = true, ForeColor = Color.FromArgb(150, 150, 150), Font = new Font("Microsoft YaHei UI", 8f) });
            scanBtn = new Button { Text = "扫描设备", Bounds = new Rectangle(14, 78, 96, 26), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, TabStop = false };
            scanBtn.FlatAppearance.BorderSize = 0;
            scanBtn.Click += (s, e) => DoScan();
            Controls.Add(scanBtn);
            status = new Label { Text = "", Location = new Point(118, 84), AutoSize = true, ForeColor = Color.FromArgb(150, 150, 150), Font = new Font("Microsoft YaHei UI", 8f) };
            Controls.Add(status);
            list = new ListBox { Bounds = new Rectangle(14, 112, Width - 28, 112), BackColor = Color.FromArgb(24, 24, 24), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Microsoft YaHei UI", 8.5f), IntegralHeight = false };
            list.SelectedIndexChanged += (s, e) => { int i = list.SelectedIndex; if (i >= 0 && i < cands.Count && !nameBox.Focused) nameBox.Text = cands[i].Name; };
            Controls.Add(list);
            Controls.Add(new Label { Text = "名称", Location = new Point(14, 234), AutoSize = true });
            nameBox = new TextBox { Bounds = new Rectangle(56, 231, 180, 24), BackColor = Color.FromArgb(24, 24, 24), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(nameBox);
            addBtn = new Button { Text = "添加", Bounds = new Rectangle(248, 230, 96, 26), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(30, 90, 60), ForeColor = Color.White, Enabled = false, TabStop = false };
            addBtn.FlatAppearance.BorderSize = 0;
            addBtn.Click += (s, e) => DoAdd();
            Controls.Add(addBtn);
            Controls.Add(new Label { Text = "休眠中的设备读不到 — 边动设备边扫描", Location = new Point(14, 266), AutoSize = true, ForeColor = Color.FromArgb(110, 110, 110), Font = new Font("Microsoft YaHei UI", 7.5f) });
            SetRounded();
        }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x00000080; return cp; }
        }
        void SetRounded()
        {
            using (var p = new GraphicsPath())
            {
                int r = 10;
                p.AddArc(0, 0, r, r, 180, 90);
                p.AddArc(Width - r - 1, 0, r, r, 270, 90);
                p.AddArc(Width - r - 1, Height - r - 1, r, r, 0, 90);
                p.AddArc(0, Height - r - 1, r, r, 90, 90);
                p.CloseFigure();
                Region = new Region(p);
            }
        }
        void DoScan()
        {
            if (Interlocked.Exchange(ref scanning, 1) != 0) return;
            scanBtn.Enabled = false; scanBtn.Text = "扫描中…"; status.Text = "请持续动一动设备…";
            list.Items.Clear(); cands = new List<Candidate>(); addBtn.Enabled = false;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                List<Candidate> res = null;
                try { res = scanner(); } catch { }
                BeginInvoke((Action)(() =>
                {
                    scanning = 0;
                    scanBtn.Enabled = true; scanBtn.Text = "重新扫描";
                    if (res == null) { status.Text = "扫描失败"; return; }
                    cands = res;
                    if (res.Count == 0) { status.Text = "没找到 — 唤醒设备后再试"; return; }
                    status.Text = "找到 " + res.Count + " 个候选";
                    foreach (var c in res)
                        list.Items.Add((c.Exists ? "[已知] " : "[新] ") + c.Name + "  " + c.VidPid + " · " + c.Proto + (c.Pct >= 0 && c.Pct <= 100 ? " · " + c.Pct + "%" : ""));
                    list.SelectedIndex = 0;
                    nameBox.Text = res[0].Name;
                    addBtn.Enabled = true;
                }));
            });
        }
        void DoAdd()
        {
            int i = list.SelectedIndex;
            if (i < 0 || i >= cands.Count) return;
            string nm = nameBox.Text.Trim();
            if (nm.Length == 0) nm = cands[i].Name;
            SaveCustom(cands[i].VidPid, nm);
            if (OnAdded != null) OnAdded();
            status.Text = "已添加 " + nm + " ✓ 可关掉此窗口";
        }
    }

    // 重命名对话框: 空名=恢复内置名称
    class RenameForm : Form
    {
        public bool Saved;
        string key;
        TextBox box;
        public RenameForm(string vidPid, string currentName)
        {
            key = vidPid;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false; TopMost = true;
            BackColor = Color.FromArgb(32, 32, 32); ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9f);
            Width = 300; Height = 132;
            Controls.Add(new Label { Text = "重命名设备", Location = new Point(14, 10), AutoSize = true, Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold) });
            Controls.Add(new Label { Text = vidPid + (string.IsNullOrEmpty(currentName) ? "" : " · 当前: " + currentName), Location = new Point(14, 38), AutoSize = true, ForeColor = Color.FromArgb(150, 150, 150), Font = new Font("Microsoft YaHei UI", 8f) });
            box = new TextBox { Bounds = new Rectangle(14, 60, 272, 24), BackColor = Color.FromArgb(24, 24, 24), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = currentName ?? "" };
            Controls.Add(box);
            var ok = new Button { Text = "确定", Bounds = new Rectangle(170, 92, 58, 26), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(30, 90, 60), ForeColor = Color.White, TabStop = false };
            ok.FlatAppearance.BorderSize = 0; ok.Click += (s, e) => { DoSave(); Close(); };
            Controls.Add(ok);
            var cancel = new Button { Text = "取消", Bounds = new Rectangle(232, 92, 56, 26), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, TabStop = false };
            cancel.FlatAppearance.BorderSize = 0; cancel.Click += (s, e) => Close();
            Controls.Add(cancel);
            box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { DoSave(); Close(); } else if (e.KeyCode == Keys.Escape) { Close(); } };
            Controls.Add(new Label { Text = "清空并确定 = 恢复内置名称", Location = new Point(14, 98), AutoSize = true, ForeColor = Color.FromArgb(110, 110, 110), Font = new Font("Microsoft YaHei UI", 7.5f) });
            SetRounded();
        }
        void DoSave()
        {
            string t = box.Text.Trim();
            if (t.Length == 0) RemoveCustom(key);
            else SaveCustom(key, t);
            Saved = true;
        }
        void SetRounded()
        {
            using (var p = new GraphicsPath())
            {
                int r = 10;
                p.AddArc(0, 0, r, r, 180, 90);
                p.AddArc(Width - r - 1, 0, r, r, 270, 90);
                p.AddArc(Width - r - 1, Height - r - 1, r, r, 0, 90);
                p.AddArc(0, Height - r - 1, r, r, 90, 90);
                p.CloseFigure();
                Region = new Region(p);
            }
        }
    }

    class BarPanel : Control
    {
        public int Pct; public bool Fresh;
        public BarPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Color.FromArgb(32, 32, 32);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var bd = new Pen(Color.FromArgb(90, 90, 90))) g.DrawRectangle(bd, 0, 0, Width - 1, Height - 1);
            if (Pct > 0)
            {
                int w = (int)Math.Round((Width - 2) * Pct / 100.0); if (w < 2) w = 2;
                Color c = !Fresh ? Color.FromArgb(140, 140, 140) : Pct <= 20 ? Color.FromArgb(255, 90, 90) : Pct <= 50 ? Color.FromArgb(255, 190, 60) : Color.FromArgb(90, 220, 120);
                using (var fg = new SolidBrush(c)) g.FillRectangle(fg, 1, 1, w, Height - 2);
            }
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        if (Array.IndexOf(args, "--console") >= 0)
        {
            try { AttachConsole(0xFFFFFFFF); } catch { }
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            InitModels();
            var list = Poll().Results;
            Console.WriteLine("BatteryTray 探测结果 " + DateTime.Now.ToString("HH:mm:ss"));
            if (list.Count == 0) Console.WriteLine("(本轮未读到任何设备)");
            foreach (var r in list)
                Console.WriteLine(string.Format("  {0}  {1}%  {2}  充电={3}  [{4}]", r.Name, r.Pct, r.Mode, r.Charging == 1 ? "是" : (r.Charging == 0 ? "否" : "未知"), r.Key));
            return;
        }
        var wi = System.Security.Principal.WindowsIdentity.GetCurrent();
        if (!new System.Security.Principal.WindowsPrincipal(wi).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
        {
            MessageBox.Show("需要管理员权限才能显示托盘图标(当前系统限制普通权限程序的托盘注册)。\n请右键 BatteryTray.exe → 以管理员身份运行。\n开机自启由计划任务 BatteryTray 管理(登录时自动提权,不弹UAC)。", "外设电量", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        bool created;
        var mutex = new Mutex(true, "BatteryTray_SingleInstance_MchoseAtk", out created);
        if (!created) { MessageBox.Show("BatteryTray 已在运行中。", "外设电量", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        Log.Init();
        Log.W("启动 v1.3 (管理员, pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + ")");
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApp());
        GC.KeepAlive(mutex);
    }
}
