using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CouchWorkstation.Wii;

/// <summary>Minimal Win32 HID access: enumerate device paths, open, read/write raw reports.</summary>
public sealed class HidDevice : IDisposable
{
    public string Path { get; }
    public int InputReportLength { get; }
    public int OutputReportLength { get; }

    // Two overlapped handles: one with a permanently pending read, one for writes (with timeout),
    // so writes never contend with the read and can never block the caller forever.
    private readonly FileStream _read;
    private readonly FileStream _write;

    private HidDevice(string path, SafeFileHandle read, SafeFileHandle write, int inLen, int outLen)
    {
        Path = path;
        InputReportLength = inLen;
        OutputReportLength = outLen;
        _read = new FileStream(read, FileAccess.Read, 0, isAsync: true);
        _write = new FileStream(write, FileAccess.Write, 0, isAsync: true);
    }

    public static List<string> Enumerate(string vidPidFilter)
    {
        var result = new List<string>();
        Native.HidD_GetHidGuid(out var guid);
        var set = Native.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
        try
        {
            var ifData = new Native.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<Native.SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref ifData); i++)
            {
                Native.SetupDiGetDeviceInterfaceDetail(set, ref ifData, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                var buf = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
                    if (Native.SetupDiGetDeviceInterfaceDetail(set, ref ifData, buf, size, out _, IntPtr.Zero))
                    {
                        var path = Marshal.PtrToStringUni(buf + 4)!;
                        if (path.Contains(vidPidFilter, StringComparison.OrdinalIgnoreCase)) result.Add(path);
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(set); }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    public static HidDevice Open(string path)
    {
        var r = OpenHandle(path);
        SafeFileHandle w;
        try { w = OpenHandle(path); }
        catch { r.Dispose(); throw; }

        int inLen = 0, outLen = 0;
        if (Native.HidD_GetPreparsedData(r, out var pp))
        {
            if (Native.HidP_GetCaps(pp, out var caps) == Native.HIDP_STATUS_SUCCESS)
            {
                inLen = caps.InputReportByteLength;
                outLen = caps.OutputReportByteLength;
            }
            Native.HidD_FreePreparsedData(pp);
        }
        return new HidDevice(path, r, w, inLen, outLen);
    }

    private static SafeFileHandle OpenHandle(string path)
    {
        var h = Native.CreateFile(path, Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateFile failed for {path}");
        return h;
    }

    /// <summary>Reads one input report.</summary>
    public async Task<byte[]> ReadAsync(CancellationToken ct)
    {
        var buf = new byte[InputReportLength];
        int n = await _read.ReadAsync(buf, ct);
        return n == buf.Length ? buf : buf[..n];
    }

    /// <summary>Writes an output report, zero-padded to the device's output report length.</summary>
    public void Write(ReadOnlySpan<byte> report, int timeoutMs = 500)
    {
        var buf = new byte[Math.Max(OutputReportLength, report.Length)];
        report.CopyTo(buf);
        var t = _write.WriteAsync(buf).AsTask();
        if (!t.Wait(timeoutMs)) throw new TimeoutException("HID write timed out");
    }

    public void Dispose()
    {
        _write.Dispose();
        _read.Dispose();
    }

    private static class Native
    {
        public const int DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
        public const int HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr pp);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr pp, out HIDP_CAPS caps);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll")]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, uint idx, ref SP_DEVICE_INTERFACE_DATA d);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);
    }
}
