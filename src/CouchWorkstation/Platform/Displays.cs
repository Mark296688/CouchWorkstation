using System.Runtime.InteropServices;

namespace CouchWorkstation.Platform;

/// <summary>A Windows display in physical pixels of the virtual desktop (the process is per-monitor DPI aware).</summary>
public sealed record DisplayInfo(string DeviceName, string FriendlyName, Rectangle Bounds, bool Primary, double RefreshHz, int ScalePercent)
{
    public override string ToString() =>
        $"{FriendlyName} – {Bounds.Width}×{Bounds.Height}, {RefreshHz:0} Hz, {ScalePercent} %{(Primary ? ", primär" : "")}";
}

public static class Displays
{
    public static List<DisplayInfo> GetAll()
    {
        var names = QueryNames();
        var list = new List<DisplayInfo>();
        foreach (var s in Screen.AllScreens)
        {
            names.TryGetValue(s.DeviceName, out var info);
            int dpi = 96;
            var hmon = MonitorFromPoint(new POINT { X = s.Bounds.X + s.Bounds.Width / 2, Y = s.Bounds.Y + s.Bounds.Height / 2 }, 2);
            if (GetDpiForMonitor(hmon, 0, out uint dx, out _) == 0) dpi = (int)dx;
            var friendly = string.IsNullOrWhiteSpace(info.Friendly) ? (s.Primary ? "Internes Display" : s.DeviceName) : info.Friendly;
            list.Add(new DisplayInfo(s.DeviceName, friendly, s.Bounds, s.Primary, info.Hz, dpi * 100 / 96));
        }
        return list;
    }

    /// <summary>Finds the configured display (by EDID name first, then device name); otherwise the largest non-primary one.</summary>
    public static DisplayInfo? Resolve(string? deviceName, string? friendlyName, out bool matchedConfig)
    {
        var all = GetAll();
        var hit = (friendlyName != null ? all.FirstOrDefault(d => d.FriendlyName == friendlyName && d.DeviceName == deviceName)
                                          ?? all.FirstOrDefault(d => d.FriendlyName == friendlyName) : null)
                  ?? (deviceName != null ? all.FirstOrDefault(d => d.DeviceName == deviceName) : null);
        matchedConfig = hit != null;
        return hit
               ?? all.Where(d => !d.Primary).OrderByDescending(d => d.Bounds.Width * d.Bounds.Height).FirstOrDefault()
               ?? all.FirstOrDefault();
    }

    private static Dictionary<string, (string Friendly, double Hz)> QueryNames()
    {
        var result = new Dictionary<string, (string, double)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint np, out uint nm) != 0) return result;
            var paths = new DISPLAYCONFIG_PATH_INFO[np];
            var modes = new DISPLAYCONFIG_MODE_INFO[nm];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return result;
            for (int i = 0; i < np; i++)
            {
                var p = paths[i];
                var src = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                src.header.type = 1; // GET_SOURCE_NAME
                src.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                src.header.adapterId = p.sourceInfo.adapterId;
                src.header.id = p.sourceInfo.id;
                if (DisplayConfigGetDeviceInfo(ref src) != 0) continue;

                var tgt = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                tgt.header.type = 2; // GET_TARGET_NAME
                tgt.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
                tgt.header.adapterId = p.targetInfo.adapterId;
                tgt.header.id = p.targetInfo.id;
                string friendly = DisplayConfigGetDeviceInfo(ref tgt) == 0 ? tgt.monitorFriendlyDeviceName : "";

                var rr = p.targetInfo.refreshRate;
                double hz = rr.Denominator == 0 ? 0 : (double)rr.Numerator / rr.Denominator;
                result[src.viewGdiDeviceName] = (friendly, hz);
            }
        }
        catch (Exception e) { Log.Warn($"Display name query failed: {e.Message}"); }
        return result;
    }

    private const uint QDC_ONLY_ACTIVE_PATHS = 2;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct LUID { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_RATIONAL { public uint Numerator, Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id, modeInfoIdx, statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id, modeInfoIdx, outputTechnology, rotation, scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO { public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo; public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo; public uint flags; }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct DISPLAYCONFIG_MODE_INFO { public uint infoType, id; public LUID adapterId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public uint type, size; public LUID adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags, outputTechnology;
        public ushort edidManufactureId, edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPath, out uint numMode);
    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPath, [Out] DISPLAYCONFIG_PATH_INFO[] paths,
        ref uint numMode, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME req);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME req);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmon, int type, out uint dpiX, out uint dpiY);
}
