using System.Diagnostics;

namespace CouchWorkstation.Wii;

/// <summary>
/// Talks to a Wii Remote through a Mayflash DolphinBar in mode 4 (each of the 4 slots appears as an
/// original Wii Remote HID device, VID 057E / PID 0306). Finds the slot with a connected remote,
/// enables the IR camera and streams report 0x33 (buttons + accelerometer + 12 byte IR) on a
/// dedicated thread. Reconnects automatically when the remote goes away or comes back.
/// </summary>
public sealed class WiiRemote : IDisposable
{
    private const string VidPid = "vid_057e&pid_0306";

    public event Action<WiiReport>? ReportReceived;
    public event Action<bool>? ConnectionChanged;

    public bool Connected { get; private set; }
    public int BatteryPercent { get; private set; } = -1;
    public string Slot { get; private set; } = "-";

    private readonly Thread _thread;
    private volatile bool _stop;
    private HidDevice? _dev;
    private int _slotIndex;
    private long _lastInit;

    public WiiRemote()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "WiiReader", Priority = ThreadPriority.AboveNormal };
    }

    public void Start() => _thread.Start();

    private static long Now => Environment.TickCount64;

    private void Run()
    {
        Task<byte[]>? pending = null;
        long lastReport = 0, lastProbe = 0;
        while (!_stop)
        {
            try
            {
                if (_dev == null)
                {
                    pending = null;
                    _dev = FindRemote();
                    if (_dev == null) { SetConnected(false); Thread.Sleep(1000); continue; }
                    InitIr();
                    lastReport = Now;
                }

                pending ??= _dev.ReadAsync(CancellationToken.None);
                if (!pending.Wait(250))
                {
                    long idle = Now - lastReport;
                    if (idle > 1000) SetConnected(false);
                    if (idle > 6000)
                    {
                        Log.Info("Wii: no reports for 6 s, rescanning DolphinBar slots");
                        CloseDevice();
                        continue;
                    }
                    if (idle > 1000 && Now - lastProbe > 1000)
                    {
                        _dev.Write([0x15, 0x00]); // status request: answered as soon as the remote is back
                        lastProbe = Now;
                    }
                    continue;
                }

                var r = pending.Result;
                pending = null;
                lastReport = Now;
                Handle(r);
            }
            catch (Exception e)
            {
                if (_stop) break;
                Log.Warn($"Wii: HID error ({e.GetBaseException().Message}), reconnecting");
                CloseDevice();
                pending = null;
                SetConnected(false);
                Thread.Sleep(1000);
            }
        }
    }

    private HidDevice? FindRemote()
    {
        var paths = HidDevice.Enumerate(VidPid);
        if (paths.Count == 0)
        {
            Log.Debug("Wii: no DolphinBar mode-4 interfaces found (DolphinBar unplugged or not in mode 4?)");
            return null;
        }
        for (int i = 0; i < paths.Count; i++)
        {
            HidDevice? dev = null;
            try
            {
                dev = HidDevice.Open(paths[i]);
                dev.Write([0x15, 0x00]);
                long deadline = Now + 400;
                var t = dev.ReadAsync(CancellationToken.None);
                while (true)
                {
                    long remaining = deadline - Now;
                    if (remaining <= 0 || !t.Wait((int)remaining)) break;
                    var r = t.Result;
                    if (r[0] == 0x20)
                    {
                        _slotIndex = SlotIndex(paths[i], i);
                        Slot = $"DolphinBar Slot {_slotIndex + 1}";
                        ParseStatus(r);
                        Log.Info($"Wii: remote found on {Slot} (battery {BatteryPercent}%), path {paths[i]}");
                        return dev;
                    }
                    t = dev.ReadAsync(CancellationToken.None);
                }
            }
            catch (Exception e)
            {
                Log.Debug($"Wii: probing {paths[i]} failed: {e.GetBaseException().Message}");
            }
            dev?.Dispose();
        }
        return null;
    }

    private static int SlotIndex(string path, int fallback)
    {
        int p = path.IndexOf("&mi_", StringComparison.OrdinalIgnoreCase);
        return p >= 0 && int.TryParse(path.AsSpan(p + 4, 2), out var mi) ? mi : fallback;
    }

    private void Handle(byte[] r)
    {
        switch (r[0])
        {
            case 0x33:
                SetConnected(true);
                ReportReceived?.Invoke(Parse(r));
                break;
            case 0x20:
                ParseStatus(r);
                SetConnected(true);
                // A status report resets the reporting mode; if the IR camera is off the remote was re-paired.
                if ((r[3] & 0x08) == 0) InitIr(); else SetReportMode();
                break;
            case 0x22:
                if (r[4] != 0) Log.Debug($"Wii: write ack error for report 0x{r[3]:X2}: {r[4]}");
                break;
            case 0x21:
                break;
            default:
                // Remote (re)connected with its default reporting mode -> configure IR again.
                if (Now - _lastInit > 1000)
                {
                    Log.Info($"Wii: unexpected report 0x{r[0]:X2}, re-initialising IR camera");
                    InitIr();
                }
                break;
        }
    }

    private static WiiReport Parse(byte[] r)
    {
        long ts = Stopwatch.GetTimestamp();
        // The bits not masked here carry accelerometer LSBs, not buttons.
        var buttons = (WiiButtons)(((r[1] & 0x1F) << 8) | (r[2] & 0x9F));
        var dots = new List<IrDot>(4);
        for (int i = 0; i < 4; i++)
        {
            int o = 6 + i * 3;
            if (r[o] == 0xFF && r[o + 1] == 0xFF && r[o + 2] == 0xFF) continue;
            int x = r[o] | ((r[o + 2] >> 4) & 3) << 8;
            int y = r[o + 1] | ((r[o + 2] >> 6) & 3) << 8;
            if (x >= 1023 || y >= 1023) continue;
            dots.Add(new IrDot(x, y, r[o + 2] & 0x0F));
        }
        return new WiiReport(ts, buttons, dots, r[3], r[4], r[5]);
    }

    private void ParseStatus(byte[] r) => BatteryPercent = Math.Min(100, r[6] * 100 / 200);

    private void InitIr()
    {
        var d = _dev!;
        _lastInit = Now;
        d.Write([0x11, (byte)(0x10 << _slotIndex)]); Pause(); // LED matching the DolphinBar slot
        d.Write([0x13, 0x04]); Pause();                     // IR pixel clock on
        d.Write([0x1A, 0x04]); Pause();                     // IR logic on
        WriteReg(0xB00030, 0x08);
        WriteReg(0xB00000, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x90, 0x00, 0xC0); // sensitivity block 1 (wiibrew level 3)
        WriteReg(0xB0001A, 0x40, 0x00);                                           // sensitivity block 2
        WriteReg(0xB00033, 0x03);                                                 // extended mode (12 byte IR)
        WriteReg(0xB00030, 0x08);
        SetReportMode();
        Log.Info("Wii: IR camera initialised (extended mode, continuous report 0x33)");
    }

    private void SetReportMode() => _dev!.Write([0x12, 0x04, 0x33]);

    private void WriteReg(int addr, params byte[] data)
    {
        var rep = new byte[22];
        rep[0] = 0x16;
        rep[1] = 0x04; // control registers
        rep[2] = (byte)(addr >> 16); rep[3] = (byte)(addr >> 8); rep[4] = (byte)addr;
        rep[5] = (byte)data.Length;
        data.CopyTo(rep, 6);
        _dev!.Write(rep);
        Pause();
    }

    private static void Pause() => Thread.Sleep(25);

    private void SetConnected(bool connected)
    {
        if (Connected == connected) return;
        Connected = connected;
        Log.Info(connected ? "Wii: connected" : "Wii: disconnected / no reports");
        ConnectionChanged?.Invoke(connected);
    }

    private void CloseDevice()
    {
        var d = _dev;
        _dev = null;
        d?.Dispose();
    }

    public void Dispose()
    {
        _stop = true;
        var d = _dev;
        if (d != null)
        {
            try
            {
                d.Write([0x12, 0x00, 0x30], 200); // back to buttons-only reporting
                d.Write([0x13, 0x00], 200);       // IR camera off (saves the remote's battery)
                d.Write([0x1A, 0x00], 200);
            }
            catch { /* remote may already be gone */ }
        }
        CloseDevice();
        if (_thread.IsAlive) _thread.Join(1500);
    }
}
