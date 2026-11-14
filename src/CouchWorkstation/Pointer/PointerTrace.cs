using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace CouchWorkstation.Pointer;

/// <summary>
/// Records every pointer sample for a fixed duration and writes it as CSV into the log folder,
/// so jitter and lag can be analysed and the smoothing tuned against real data.
/// </summary>
public sealed class PointerTrace
{
    private readonly StringBuilder _sb = new();
    private readonly long _start = Stopwatch.GetTimestamp();
    private readonly long _end;
    public bool Finished { get; private set; }

    public PointerTrace(int seconds)
    {
        _end = _start + Stopwatch.Frequency * seconds;
        _sb.AppendLine("t_ms;dots;aim_x;aim_y;roll_deg;raw_x;raw_y;out_x;out_y;buttons");
        Log.Info($"Pointer trace started ({seconds} s)");
    }

    /// <summary>Returns false once the trace is complete (it is then saved in the background).</summary>
    public bool Add(long ts, int dots, double ax, double ay, double roll, double rx, double ry, double ox, double oy, int buttons)
    {
        if (Finished) return false;
        var c = CultureInfo.InvariantCulture;
        _sb.Append(((ts - _start) * 1000.0 / Stopwatch.Frequency).ToString("F1", c)).Append(';')
           .Append(dots).Append(';')
           .Append(ax.ToString("F2", c)).Append(';').Append(ay.ToString("F2", c)).Append(';')
           .Append(roll.ToString("F2", c)).Append(';')
           .Append(rx.ToString("F1", c)).Append(';').Append(ry.ToString("F1", c)).Append(';')
           .Append(ox.ToString("F1", c)).Append(';').Append(oy.ToString("F1", c)).Append(';')
           .Append(buttons).AppendLine();
        if (ts < _end) return true;

        Finished = true;
        var text = _sb.ToString();
        var path = Path.Combine(Log.Directory, $"pointer-trace-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        Task.Run(() =>
        {
            try { File.WriteAllText(path, text); Log.Info($"Pointer trace saved: {path}"); }
            catch (Exception e) { Log.Error($"Pointer trace could not be saved: {e.Message}"); }
        });
        return false;
    }
}
