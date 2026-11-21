// HidScan.cs - 外设电量 HID 扫描探测工具 (MCHOSE / ATK / Compx-Nordic / BLE)
// 编译: csc /nologo /optimize+ /platform:x64 /out:HidScan.exe HidScan.cs
// 只读探测: 仅发送各协议公开的电量查询帧, 不写配置/不改设备行为
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

class HidScan
{
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
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Locate_DevNode(out uint inst, string id, uint flags);
    [DllImport("cfgmgr32.dll")] static extern uint CM_Get_DevNode_Property(uint inst, ref DEVPROPKEY key, out uint type, byte[] buf, ref uint size, uint flags);

    static readonly Guid HID_GUID = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");
    static readonly DEVPROPKEY BATTERY_KEY = new DEVPROPKEY { fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), pid = 2 };
    const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10, DIGCF_ALLCLASSES = 0x04;

    static StringWriter log = new StringWriter();
    static int diagOpenFails = 0;
    static int diagPaths = 0;
    static void P(string s) { Console.WriteLine(s); log.WriteLine(s); }
    static string Hex(byte[] b, int n) { if (b == null) return "(null)"; int k = Math.Min(n, b.Length); return BitConverter.ToString(b, 0, k).Replace("-", " "); }
    static string Hex(byte[] b) { return Hex(b, 24); }

    class HidDev
    {
        public string Path, InstanceId, Product, Manufacturer;
        public ushort Vid, Pid, Usage, UsagePage, InLen, OutLen, FeatLen;
        public bool IsVendorPage { get { return UsagePage >= 0xFF00; } }
        public string Key { get { return Vid.ToString("X4") + ":" + Pid.ToString("X4") + " p" + UsagePage.ToString("X4"); } }
    }

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
        if (h == IntPtr.Zero || h == new IntPtr(-1)) { P("  [诊断] SetupDiGetClassDevs 失败 err=" + Marshal.GetLastWin32Error()); return list; }
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
            if (diagPaths++ < 3) P("  [诊断] path=" + path);
            Marshal.FreeHGlobal(buf);
            if (!ok || path == null) continue;
            var sb = new StringBuilder(512); uint r2;
            string inst = SetupDiGetDeviceInstanceId(h, ref di, sb, 512, out r2) ? sb.ToString() : "";
            IntPtr fh = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (fh == IntPtr.Zero || fh == new IntPtr(-1)) { if (diagOpenFails++ < 3) P("  [诊断] 打开HID接口失败 err=" + Marshal.GetLastWin32Error()); continue; }
            var dev = new HidDev { Path = path, InstanceId = inst };
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

    // ---------------- 协议探测 ----------------
    // A5/AX5: VID 0x2023, 65B feature, buf[3]=2 buf[4]=2 buf[6]=0x83; resp buf[1]=0xA1, buf[7]充电 buf[8]电量
    static string ProbeA5(IntPtr h, int featLen)
    {
        int len = Math.Max(featLen, 65);
        int[] delays = { 50, 100, 200, 400 };
        byte[] last = null;
        foreach (int d in delays)
        {
            byte[] cmd = new byte[len]; cmd[3] = 2; cmd[4] = 2; cmd[6] = 0x83;
            if (!HidD_SetFeature(h, cmd, len)) continue;
            Thread.Sleep(d);
            byte[] rsp = new byte[len]; rsp[3] = 2;
            if (!HidD_GetFeature(h, rsp, len)) continue;
            last = rsp;
            if (rsp[1] == 0xA1 && rsp[8] <= 100)
                return string.Format("[OK] A5/AX5 协议: 电量={0}% 充电={1} raw={2}", rsp[8], rsp[7] == 1 ? "是" : "否", Hex(rsp, 12));
        }
        return "[--] A5/AX5 无有效应答(0xA1) last=" + Hex(last, 12);
    }

    // 旧A7: VID 0x5253 page FF01, feature 0x11 [F9 FF..], resp去前2字节XOR FF, dec[11]=电量
    static string ProbeA7(IntPtr h, int featLen)
    {
        int len = Math.Max(featLen, 65);
        byte[] cmd = new byte[len]; cmd[0] = 0x11; cmd[1] = 0xF9;
        for (int i = 2; i < len; i++) cmd[i] = 0xFF;
        if (!HidD_SetFeature(h, cmd, len)) return "[--] A7 SetFeature(0x11) 失败 err=" + Marshal.GetLastWin32Error();
        Thread.Sleep(150);
        byte[] rsp = new byte[len]; rsp[0] = 0x11;
        if (!HidD_GetFeature(h, rsp, len)) return "[--] A7 GetFeature(0x11) 失败 err=" + Marshal.GetLastWin32Error();
        byte[] dec = new byte[len - 2];
        for (int i = 0; i < dec.Length; i++) dec[i] = (byte)(rsp[i + 2] ^ 0xFF);
        int pct = dec[11];
        string tail = Encoding.ASCII.GetString(dec, 12, Math.Min(16, dec.Length - 12)).TrimEnd('\0');
        if (pct <= 100) return string.Format("[OK] A7(RealTek feature) 协议: 电量={0}% 型号串=\"{1}\" raw={2}", pct, tail, Hex(rsp, 16));
        return "[--] A7 feature 回包异常 dec=" + Hex(dec, 16);
    }

    // RealTek 推送式 (L7 Pro 等): nudge 后等 input 0x13/0x1D, res[5]^FF=电量 res[4]^FF=有线
    static string ProbeRealtekPush(IntPtr h, int inLen)
    {
        byte[] cmd = new byte[65]; cmd[0] = 0x11; cmd[1] = 0xF9;
        for (int i = 2; i < 65; i++) cmd[i] = 0xFF;
        HidD_SetFeature(h, cmd, 65); // nudge, 失败也无妨
        var deadline = DateTime.UtcNow.AddSeconds(3);
        byte[] last = null;
        while (DateTime.UtcNow < deadline)
        {
            byte[] r = ReadInput(h, inLen > 0 ? inLen : 65, 250);
            if (r == null) continue;
            last = r;
            if (r.Length > 5 && r[0] == 0x13 && r[1] == 0x1D)
            {
                int pct = r[5] ^ 0xFF, wired = r[4] ^ 0xFF;
                if (pct <= 100) return string.Format("[OK] RealTek推送(L7系) 协议: 电量={0}% 有线={1} raw={2}", pct, wired == 1 ? "是" : "否", Hex(r, 16));
            }
        }
        return "[--] RealTek推送 3秒内无 0x13/0x1D 帧 last=" + Hex(last, 16);
    }

    // G87 Compx 键盘: VID 0x41E4 page FF02, write 13 4A 01.., input r[0]=13 r[1]=4A, r[5]=电量
    static string ProbeG87(IntPtr h, int inLen, int outLen)
    {
        Drain(h, inLen);
        byte[] cmd = new byte[20]; cmd[0] = 0x13; cmd[1] = 0x4A; cmd[2] = 0x01;
        if (!SendOutput(h, cmd, outLen > 0 ? outLen : 20)) return "[--] G87 写命令失败 err=" + Marshal.GetLastWin32Error();
        var deadline = DateTime.UtcNow.AddMilliseconds(1500);
        byte[] last = null;
        while (DateTime.UtcNow < deadline)
        {
            byte[] r = ReadInput(h, inLen > 0 ? inLen : 65, 200);
            if (r == null) continue;
            last = r;
            if (r.Length >= 6 && r[0] == 0x13 && r[1] == 0x4A && r[5] <= 100)
                return string.Format("[OK] G87(Compx) 协议: 电量={0}% raw={1}", r[5], Hex(r, 12));
        }
        return "[--] G87 无应答(可能休眠) last=" + Hex(last, 12);
    }

    // Gmouse: write 65B [1]=55 [2]=30 [3]=A5 [4]=0B [5]=2E [6..8]=01; input 找 AA 30, +8电量 +9充电
    static string ProbeGmouse(IntPtr h, int inLen, int outLen)
    {
        byte[] cmd = new byte[65];
        cmd[1] = 0x55; cmd[2] = 0x30; cmd[3] = 0xA5; cmd[4] = 0x0B; cmd[5] = 0x2E; cmd[6] = 1; cmd[7] = 1; cmd[8] = 1;
        if (!SendOutput(h, cmd, outLen > 0 ? outLen : 65)) return "[--] Gmouse 写命令失败 err=" + Marshal.GetLastWin32Error();
        var deadline = DateTime.UtcNow.AddMilliseconds(1800);
        byte[] last = null;
        while (DateTime.UtcNow < deadline)
        {
            byte[] r = ReadInput(h, inLen > 0 ? inLen : 65, 250);
            if (r == null) continue;
            last = r;
            for (int i = 0; i + 9 < r.Length; i++)
            {
                if (r[i] == 0xAA && r[i + 1] == 0x30 && r[i + 8] <= 100)
                    return string.Format("[OK] Gmouse 协议: 电量={0}% 充电={1} raw={2}", r[i + 8], r[i + 9] != 0 ? "是" : "否", Hex(r, 20));
            }
        }
        return "[--] Gmouse 无 AA30 应答 last=" + Hex(last, 20);
    }

    // HUB 新型号: 有线 feature 06 87 00 00 01 00 02 00 / 无线 13 4A 01; Bbe/ilt 解析
    static string ParseHub(byte[] buf, bool wired)
    {
        if (buf == null || buf.Length < 8) return null;
        int s = (buf[0] == 0x06 || buf[0] == 0x13) ? 1 : 0; // strip report id
        // ilt: command=10 type=5 (有线紧邻, 无线隔3字节)
        for (int i = s; i + 3 < buf.Length; i++)
        {
            if (buf[i] == 0x0A)
            {
                int t = -1;
                if (i + 1 < buf.Length && buf[i + 1] == 0x05) t = i + 1;
                else if (i + 4 < buf.Length && buf[i + 1] == 0 && buf[i + 2] == 0 && buf[i + 3] == 0 && buf[i + 4] == 0x05) t = i + 4;
                if (t >= 0 && t + 2 < buf.Length && buf[t + 1] <= 100)
                    return string.Format("电量={0}% (ilt, packed={1:X2})", buf[t + 1], buf[t + 2]);
            }
        }
        // Bbe: 有线 base=6 无线 base=0 (相对去report id后)
        int b = s + (wired ? 6 : 0);
        if (b + 1 < buf.Length && buf[b] <= 100 && buf[b] > 0)
            return string.Format("电量={0}% (bbe, packed={1:X2})", buf[b], buf[b + 1]);
        return null;
    }
    static string ProbeHub(IntPtr h, HidDev d)
    {
        var sb = new StringBuilder();
        int flen = d.FeatLen;
        if (flen >= 8)
        {
            byte[] w = new byte[flen]; w[0] = 0x06; w[1] = 0x87; w[4] = 0x01; w[6] = 0x02;
            if (HidD_SetFeature(h, w, flen))
            {
                Thread.Sleep(60);
                byte[] rsp = new byte[flen]; rsp[0] = 0x06;
                if (HidD_GetFeature(h, rsp, flen))
                {
                    string p = ParseHub(rsp, true) ?? ParseHub(rsp, false);
                    if (p != null) return "[OK] HUB(有线feature) 协议: " + p + " raw=" + Hex(rsp, 16);
                    sb.Append("wiredFeat=" + Hex(rsp, 12) + "; ");
                }
            }
            byte[] c = new byte[flen]; c[0] = 0x13; c[1] = 0x4A; c[2] = 0x01;
            bool sent = HidD_SetFeature(h, c, flen);
            if (!sent && d.OutLen > 0) sent = SendOutput(h, c, d.OutLen);
            if (sent)
            {
                Thread.Sleep(60);
                byte[] rsp = new byte[flen]; rsp[0] = 0x13;
                if (HidD_GetFeature(h, rsp, flen))
                {
                    string p = ParseHub(rsp, false) ?? ParseHub(rsp, true);
                    if (p != null) return "[OK] HUB(无线feature) 协议: " + p + " raw=" + Hex(rsp, 16);
                    sb.Append("wlFeat=" + Hex(rsp, 12) + "; ");
                }
            }
        }
        if (d.InLen > 0)
        {
            if (d.OutLen > 0) { byte[] c = new byte[d.OutLen]; c[0] = 0x13; c[1] = 0x4A; c[2] = 0x01; SendOutput(h, c, d.OutLen); }
            var deadline = DateTime.UtcNow.AddMilliseconds(1500);
            byte[] last = null;
            while (DateTime.UtcNow < deadline)
            {
                byte[] r = ReadInput(h, d.InLen, 250);
                if (r == null) continue;
                last = r;
                string p = ParseHub(r, false) ?? ParseHub(r, true);
                if (p != null) return "[OK] HUB(input) 协议: " + p + " raw=" + Hex(r, 16);
            }
            sb.Append("input=" + Hex(last, 12));
        }
        return "[--] HUB 全部变体失败 " + sb.ToString();
    }

    // Compx/Nordic52 (ATK F1/A9, VXE, VGN...): 17B write 08 04 .. 49; resp[6]=电量 resp[7]=有线
    static string ProbeNordic52(IntPtr h, int inLen, int outLen)
    {
        byte[] cmd = new byte[17]; cmd[0] = 0x08; cmd[1] = 0x04; cmd[16] = 0x49;
        if (!SendOutput(h, cmd, outLen > 0 ? outLen : 17)) return "[--] Nordic52 写失败 err=" + Marshal.GetLastWin32Error();
        Thread.Sleep(120);
        byte[] last = null;
        for (int i = 0; i < 6; i++)
        {
            byte[] r = ReadInput(h, inLen > 0 ? inLen : 17, 250);
            if (r == null) continue;
            last = r;
            if (r.Length >= 8 && r[0] == 0x08 && r[1] == 0x04 && r[6] <= 100)
            {
                int sum = 0; foreach (byte b in r) sum += b;
                return string.Format("[OK] Compx/Nordic52(ATK系) 协议: 电量={0}% 有线/充电={1} 校验和={2} raw={3}",
                    r[6], r[7] != 0 ? "是" : "否", (sum & 0xFF) == 0x55 ? "OK" : "BAD", Hex(r, 17));
            }
        }
        return "[--] Nordic52 无应答(鼠标可能休眠) last=" + Hex(last, 17);
    }

    // Nordic54 (ATK Zero): 64B write 08 7D 72 02 00 link 07 01; resp[1]=72 [2]=0 [5]=7 [7]=电量
    static string ProbeNordic54(IntPtr h, int inLen, int outLen, bool wired)
    {
        byte[] cmd = new byte[64];
        cmd[0] = 0x08; cmd[1] = 0x7D; cmd[2] = 0x72; cmd[3] = 0x02; cmd[5] = (byte)(wired ? 0 : 1); cmd[6] = 0x07; cmd[7] = 0x01;
        if (!SendOutput(h, cmd, outLen > 0 ? outLen : 64)) return "[--] Nordic54 写失败 err=" + Marshal.GetLastWin32Error();
        Thread.Sleep(120);
        byte[] last = null;
        for (int i = 0; i < 6; i++)
        {
            byte[] r = ReadInput(h, inLen > 0 ? inLen : 64, 250);
            if (r == null) continue;
            last = r;
            if (r.Length >= 10 && r[1] == 0x72 && r[5] == 0x07)
            {
                if (r[2] != 0) return "[--] Nordic54 鼠标未应答(status=1, 休眠/离线) raw=" + Hex(r, 12);
                int mv = r[8] | (r[9] << 8);
                return string.Format("[OK] Nordic54(ATK Zero) 协议: 电量={0}% 电压={1}mV link={2} raw={3}", r[7], mv, wired ? "有线" : "2.4G", Hex(r, 12));
            }
        }
        return "[--] Nordic54 无应答 last=" + Hex(last, 12);
    }

    // Compx 键盘 feature 通道尝试 (G98 V2 等无 20B output 集合的型号)
    static string ProbeCompxFeature(IntPtr h, int flen)
    {
        if (flen < 17) return "[--] CXFEAT feature长度不足";
        byte[] cmd = new byte[flen]; cmd[0] = 0x08; cmd[1] = 0x04; cmd[16] = 0x49;
        if (HidD_SetFeature(h, cmd, flen))
        {
            Thread.Sleep(120);
            byte[] rsp = new byte[flen]; rsp[0] = 0x08;
            if (HidD_GetFeature(h, rsp, flen) && rsp[1] == 0x04 && rsp[6] <= 100)
                return string.Format("[OK] Compx-feature(N52帧) 协议: 电量={0}% 有线/充电={1} raw={2}", rsp[6], rsp[7] != 0 ? "是" : "否", Hex(rsp, 17));
        }
        byte[] c2 = new byte[flen]; c2[0] = 0x13; c2[1] = 0x4A; c2[2] = 0x01;
        if (HidD_SetFeature(h, c2, flen))
        {
            Thread.Sleep(120);
            byte[] rsp = new byte[flen]; rsp[0] = 0x13;
            if (HidD_GetFeature(h, rsp, flen) && rsp[1] == 0x4A && rsp[5] <= 100)
                return string.Format("[OK] Compx-feature(G87帧) 协议: 电量={0}% raw={1}", rsp[5], Hex(rsp, 12));
            return "[--] CXFEAT G87帧回包=" + Hex(rsp, 12);
        }
        return "[--] CXFEAT 两种feature帧均被拒 err=" + Marshal.GetLastWin32Error();
    }

    static string ProbePassive(IntPtr h, int inLen)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        var sb = new StringBuilder(); int n = 0;
        while (DateTime.UtcNow < deadline && n < 6)
        {
            byte[] r = ReadInput(h, inLen, 400);
            if (r == null) continue;
            sb.Append(Hex(r, Math.Min(r.Length, 16))).Append(" | "); n++;
        }
        return n > 0 ? "[??] 被动收到 " + n + " 帧: " + sb.ToString() : "[--] 被动监听3秒无数据";
    }

    // 已知型号表 (VID, PID) -> 名称/模式
    static string KnownModel(ushort vid, ushort pid)
    {
        var t = new Dictionary<uint, string>();
        Action<ushort, ushort, string> add = (v, p, n) => t[(uint)(v << 16 | p)] = n;
        add(0x373B, 0x1031, "ATK F1 Ultimate (2.4G接收器)"); add(0x373B, 0x102E, "ATK F1 Ultimate (有线)");
        add(0x373B, 0x11D9, "ATK A9 Ultimate (2.4G)"); add(0x373B, 0x11B6, "ATK A9 Ultimate (有线)");
        add(0x373B, 0x1155, "ATK Zero 8k dongle-L1"); add(0x373B, 0x124F, "ATK Zero 8k dongle-L0"); add(0x373B, 0x1154, "ATK Zero (有线)");
        add(0x3554, 0xF58A, "VXE R1 Pro Max (2.4G)"); add(0x3554, 0xF58C, "VXE R1 Pro Max (有线)");
        add(0x3554, 0xF58E, "VXE R1 SE+ (2.4G)"); add(0x3554, 0xF58F, "VXE R1 SE+ (有线)");
        add(0x3554, 0xF503, "VGN F1 Pro (2.4G)"); add(0x3554, 0xF502, "VGN F1 Pro (有线)");
        add(0x3837, 0x1001, "迈从 K7 Ultra (RealTek)"); add(0x41E4, 0x2004, "迈从 2.4G 接收器 (Compx)"); add(0x41E4, 0x2203, "迈从 G98 V2 (Compx)");
        add(0x3554, 0xFB3E, "VGN F2 Pro Max (2.4G)"); add(0x3554, 0xFB3D, "VGN F2 Pro Max (有线)");
        add(0x2023, 0xF013, "迈从 A5 系列 2.4G 接收器"); add(0x2023, 0xF015, "迈从 通配接收器");
        add(0x2023, 0xF017, "迈从 A5 Pro (有线)"); add(0x2023, 0xF019, "迈从 A5 Pro Max (有线)");
        add(0x2023, 0xF024, "迈从 AX5 系列 2.4G 接收器"); add(0x2023, 0xF026, "迈从 AX5 Pro (有线)"); add(0x2023, 0xF022, "迈从 AX5 Pro Max (有线)");
        add(0x5253, 0x1021, "迈从 A7 (2.4G)"); add(0x5253, 0x1020, "迈从 L7 Pro (2.4G)"); add(0x5253, 0x00B0, "迈从 L7 Pro (有线)");
        add(0x41E4, 0x2001, "迈从 G87 键盘");
        add(0x3837, 0x1016, "迈从 V7 (2.4G)"); add(0x3837, 0x402A, "迈从 V7 (另一枚举)");
        add(0x3837, 0x418C, "迈从 G3 V2 (有线)"); add(0x3837, 0x4042, "迈从 G3 V2 Pro (有线)");
        add(0x3837, 0x4012, "迈从 A7e (有线)"); add(0x3837, 0x4013, "迈从 A7e Pro (有线)");
        add(0xA8A5, 0x2255, "迈从 G3 A (无线)");
        uint k = (uint)(vid << 16 | pid);
        return t.ContainsKey(k) ? t[k] : null;
    }

    static void ProbeDevice(HidDev d, HashSet<string> done, bool probeAll)
    {
        string model = KnownModel(d.Vid, d.Pid);
        bool isMchoseName = (d.Product + " " + d.Manufacturer).ToLower().Contains("mchose")
            || (d.Product + " " + d.Manufacturer).Contains("迈从") || (d.Product + " " + d.Manufacturer).ToLower().Contains("maicong");
        var protos = new List<string>();
        if (d.Vid == 0x2023 && d.FeatLen >= 65) protos.Add("A5");
        if (d.Vid == 0x5253 && d.UsagePage == 0xFF01) { protos.Add("A7"); protos.Add("RTPUSH"); }
        bool isRealtek = (d.Manufacturer ?? "").ToLower().Contains("realtek");
        if (isRealtek && d.UsagePage == 0xFF01 && d.FeatLen >= 20 && !protos.Contains("A7")) { protos.Add("A7"); protos.Add("RTPUSH"); }
        if (isRealtek && d.UsagePage == 0xFF0B && d.InLen >= 16) protos.Add("RTPUSH");
        if (d.Vid == 0x41E4 && d.UsagePage == 0xFF02) protos.Add("G87");
        if ((d.Vid == 0xA8A5 || d.Vid == 0xA8A4 || d.Vid == 0x3837) && d.UsagePage == 0xFF01 && (d.Usage == 0x10 || d.Usage == 0)) protos.Add("GMOUSE");
        if (d.Vid == 0x3837 && d.IsVendorPage) protos.Add("HUB");
        if (isMchoseName && d.IsVendorPage && d.Vid != 0x2023 && d.Vid != 0x5253 && !protos.Contains("HUB")) protos.Add("HUB");
        if ((d.Vid == 0x373B || d.Vid == 0x3554 || d.Vid == 0x260D || d.Vid == 0x33E4) && d.UsagePage == 0xFF02) protos.Add("N52");
        if (d.UsagePage == 0xFF05 && (d.Usage == 1 || d.Usage == 0)) protos.Add("N54");
        if (d.Vid == 0x41E4 && d.FeatLen >= 17) protos.Add("CXFEAT");
        if ((d.Vid == 0x41E4 || d.Vid == 0x3837) && d.IsVendorPage && d.InLen > 0 && d.InLen <= 8 && !protos.Contains("G87")) protos.Add("PASSIVE");
        if (protos.Count == 0 && probeAll && d.IsVendorPage) { protos.Add("N52"); protos.Add("HUB"); }
        if (protos.Count == 0) return;
        if (done.Contains(d.Key)) { P("    (同 VID/PID/页已探测, 跳过)"); return; }
        IntPtr h = OpenRW(d.Path);
        if (h == IntPtr.Zero) { P("    [!!] 无法打开接口 err=" + Marshal.GetLastWin32Error() + " (可能被官方驱动占用)"); return; }
        foreach (string pr in protos)
        {
            string r;
            switch (pr)
            {
                case "A5": r = ProbeA5(h, d.FeatLen); break;
                case "A7": r = ProbeA7(h, d.FeatLen); break;
                case "RTPUSH": r = ProbeRealtekPush(h, d.InLen); break;
                case "G87": r = ProbeG87(h, d.InLen, d.OutLen); break;
                case "GMOUSE": r = ProbeGmouse(h, d.InLen, d.OutLen); break;
                case "HUB": r = ProbeHub(h, d); break;
                case "N52": r = ProbeNordic52(h, d.InLen, d.OutLen); break;
                case "CXFEAT": r = ProbeCompxFeature(h, d.FeatLen); break;
                case "PASSIVE": r = ProbePassive(h, d.InLen); break;
                case "N54": r = ProbeNordic54(h, d.InLen, d.OutLen, false) + " | " + ProbeNordic54(h, d.InLen, d.OutLen, true); break;
                default: r = "?"; break;
            }
            P("    " + r);
            if (r.StartsWith("[OK]")) { done.Add(d.Key); break; }
        }
        CloseHandle(h);
    }

    // ---------------- 蓝牙缓存电量 ----------------
    static void ScanBle()
    {
        P("");
        P("==== 蓝牙设备系统缓存电量 (DEVPKEY_Bluetooth_Battery, 覆盖蓝牙模式) ====");
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
                else if (SetupDiGetDeviceRegistryProperty(h, ref di, 0x0, out rt, nb, 512, out req)) name = Encoding.Unicode.GetString(nb).TrimEnd('\0');
                uint inst2;
                string battery = "(无电量属性)";
                if (CM_Locate_DevNode(out inst2, inst, 0) == 0)
                {
                    var key = BATTERY_KEY; uint type; byte[] buf = new byte[16]; uint size = 16;
                    uint cr = CM_Get_DevNode_Property(inst2, ref key, out type, buf, ref size, 0);
                    if (cr == 0 && size >= 1)
                    {
                        int v = size == 1 ? buf[0] : (size == 2 ? BitConverter.ToUInt16(buf, 0) : (int)BitConverter.ToUInt32(buf, 0));
                        battery = v + "%";
                    }
                }
                P(string.Format("  [{0}] {1}  电量={2}", enumerator, name.Length > 0 ? name : inst, battery));
                P("      " + inst);
            }
            SetupDiDestroyDeviceInfoList(h);
        }
    }

    static void Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        bool probeAll = Array.IndexOf(args, "--all") >= 0;
        bool noProbe = Array.IndexOf(args, "--no-probe") >= 0;
        P("HidScan - 外设电量探测  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        P("模式: " + (noProbe ? "仅枚举" : (probeAll ? "全量探测(--all)" : "已知协议族探测")));
        P("");
        P("==== HID 接口枚举 ====");
        var devs = EnumerateHid();
        P(string.Format("共 {0} 个 HID 接口", devs.Count));
        foreach (var d in devs)
        {
            string model = KnownModel(d.Vid, d.Pid);
            P(string.Format("  VID={0:X4} PID={1:X4} page={2:X4} usage={3:X4} in={4} out={5} feat={6}  {7}{8}",
                d.Vid, d.Pid, d.UsagePage, d.Usage, d.InLen, d.OutLen, d.FeatLen,
                (d.Manufacturer + " " + d.Product).Trim(), model != null ? "  <== " + model : ""));
        }
        if (!noProbe)
        {
            P("");
            P("==== 协议探测 (只发电量查询帧) ====");
            var done = new HashSet<string>();
            foreach (var d in devs)
            {
                string model = KnownModel(d.Vid, d.Pid);
                bool interesting = d.IsVendorPage || model != null;
                if (!interesting) continue;
                P(string.Format("  >> {0:X4}:{1:X4} page={2:X4} usage={3:X4} {4}{5}", d.Vid, d.Pid, d.UsagePage, d.Usage, d.Product, model != null ? " [" + model + "]" : ""));
                try { ProbeDevice(d, done, probeAll); } catch (Exception ex) { P("    [!!] 探测异常: " + ex.GetType().Name + ": " + ex.Message); }
            }
        }
        try { ScanBle(); } catch (Exception ex) { P("[!!] BLE扫描异常: " + ex.Message); }
        P("");
        P("==== 汇总: 成功读到电量的协议 ====");
        string all = log.ToString();
        foreach (string line in all.Split('\n'))
            if (line.Contains("[OK]")) P("  " + line.Trim());
        File.WriteAllText("scan_report.txt", all, Encoding.UTF8);
        P("");
        P("报告已保存: scan_report.txt");
    }
}
