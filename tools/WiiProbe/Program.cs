using System.Diagnostics;
using CouchWorkstation.Wii;

// WiiProbe: finds the Wii Remote behind a DolphinBar (mode 4), enables IR, and dumps buttons + IR dots.
// Usage: WiiProbe [seconds]
int seconds = args.Length > 0 ? int.Parse(args[0]) : 20;

var paths = HidDevice.Enumerate("vid_057e&pid_0306");
Console.WriteLine($"Found {paths.Count} Wii HID interface(s)");

HidDevice? wii = null;
foreach (var p in paths)
{
    HidDevice dev;
    try { dev = HidDevice.Open(p); }
    catch (Exception e) { Console.WriteLine($"  open failed: {p} -> {e.Message}"); continue; }
    Console.WriteLine($"  {p}\n    in={dev.InputReportLength} out={dev.OutputReportLength}");
    try
    {
        dev.Write([0x15, 0x00]); // status request
        using var cts = new CancellationTokenSource(700);
        while (true)
        {
            var r = await dev.ReadAsync(cts.Token);
            Console.WriteLine($"    report: {BitConverter.ToString(r, 0, Math.Min(r.Length, 8))}");
            if (r[0] == 0x20)
            {
                Console.WriteLine($"    STATUS: battery=0x{r[6]:X2} flags=0x{r[3]:X2} -> Wii Remote connected on this slot");
                wii = dev;
                break;
            }
        }
    }
    catch (OperationCanceledException) { Console.WriteLine("    no response (slot empty)"); }
    catch (Exception e) { Console.WriteLine($"    write/read failed: {e.Message}"); }
    if (wii == dev) break;
    dev.Dispose();
}

if (wii == null) { Console.WriteLine("No Wii Remote responding."); return; }

// IR camera init (wiibrew), extended mode, then continuous report 0x33 (buttons + accel + 12 byte IR)
void WriteReg(int addr, params byte[] data)
{
    var rep = new byte[22];
    rep[0] = 0x16; rep[1] = 0x04;
    rep[2] = (byte)(addr >> 16); rep[3] = (byte)(addr >> 8); rep[4] = (byte)addr; rep[5] = (byte)data.Length;
    data.CopyTo(rep, 6);
    wii.Write(rep);
    Thread.Sleep(30);
}
wii.Write([0x11, 0x10]); Thread.Sleep(30);  // LED 1
wii.Write([0x13, 0x04]); Thread.Sleep(30);  // IR pixel clock
wii.Write([0x1A, 0x04]); Thread.Sleep(30);  // IR logic
WriteReg(0xB00030, 0x08);
WriteReg(0xB00000, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x90, 0x00, 0xC0); // sensitivity block 1 (level 3)
WriteReg(0xB0001A, 0x40, 0x00);                                           // sensitivity block 2
WriteReg(0xB00033, 0x03);                                                 // extended mode
WriteReg(0xB00030, 0x08);
wii.Write([0x12, 0x04, 0x33]);

if (args.Contains("wait"))
{
    Console.WriteLine("Waiting (max 10 min) for the first button press ...");
    using var waitCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
    while (true)
    {
        var r = await wii.ReadAsync(waitCts.Token);
        if (r[0] == 0x33 && ((r[1] & 0x1F) | (r[2] & 0x9F)) != 0) { Console.WriteLine($"START (first press raw={r[1]:X2}{r[2]:X2})"); break; }
        if (r[0] == 0x20) wii.Write([0x12, 0x04, 0x33]);
    }
}

Console.WriteLine($"Streaming for {seconds}s - press A / B, point at the TV ...");
var sw = Stopwatch.StartNew();
int count = 0, lastButtons = -1;
long lastPrint = 0, lastRateT = 0; int lastRateCount = 0;
using var run = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
try
{
    while (true)
    {
        var r = await wii.ReadAsync(run.Token);
        if (r[0] == 0x20) { wii.Write([0x12, 0x04, 0x33]); Console.WriteLine("status report -> re-set mode"); continue; }
        if (r[0] == 0x22) { if (r[4] != 0) Console.WriteLine($"ack err for 0x{r[3]:X2}: {r[4]}"); continue; }
        if (r[0] != 0x33) { Console.WriteLine($"report 0x{r[0]:X2}"); continue; }
        count++;
        int buttons = ((r[1] & 0x1F) << 8) | (r[2] & 0x9F); // other bits carry accel LSBs
        if (buttons != lastButtons)
        {
            Console.WriteLine($"[{sw.ElapsedMilliseconds,6}ms] buttons=0x{buttons:X4} A={(r[2] & 0x08) != 0} B={(r[2] & 0x04) != 0}");
            lastButtons = buttons;
        }
        long now = sw.ElapsedMilliseconds;
        if (now - lastPrint >= 250)
        {
            var dots = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                int o = 6 + i * 3;
                if (r[o] == 0xFF && r[o + 1] == 0xFF && r[o + 2] == 0xFF) continue;
                int x = r[o] | ((r[o + 2] >> 4) & 3) << 8;
                int y = r[o + 1] | ((r[o + 2] >> 6) & 3) << 8;
                dots.Add($"({x},{y} s{r[o + 2] & 0xF})");
            }
            double rate = (count - lastRateCount) * 1000.0 / Math.Max(1, now - lastRateT);
            Console.WriteLine($"[{now,6}ms] {rate,5:F0} Hz  raw={r[1]:X2}{r[2]:X2} acc={r[3]},{r[4]},{r[5]}  IR: {(dots.Count == 0 ? "none" : string.Join(" ", dots))}");
            lastPrint = now; lastRateT = now; lastRateCount = count;
        }
    }
}
catch (OperationCanceledException) { }
Console.WriteLine($"Done. {count} reports in {sw.Elapsed.TotalSeconds:F1}s");
wii.Dispose();
