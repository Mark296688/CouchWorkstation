using System.Diagnostics;
using CouchWorkstation.Platform;
using CouchWorkstation.Wii;

namespace CouchWorkstation.Pointer;

/// <summary>
/// Runs on the Wii reader thread for every report: IR -> aim point -> screen position -> SendInput,
/// then hands button edges to the router. Keeps simple statistics for the UI and the log.
/// </summary>
public sealed class PointerController
{
    private readonly PointerConfig _cfg;
    private readonly IrTracker _tracker = new();
    private readonly OneEuroFilter2D _filter;
    private readonly object _lock = new();
    private PointerMapping _mapping;
    private Rectangle _screen;
    private bool _hadAim;
    private long _freezeUntil;
    private (int X, int Y) _lastCursor;
    private (double X, double Y) _stable;
    private PointerTrace? _trace;

    // Short history of aim points for the calibration (averaging over the last ~150 ms).
    private readonly Queue<(long T, double X, double Y)> _aimHistory = new();

    public ButtonRouter? Router { get; set; }

    /// <summary>Pointer movement and button actions on/off ("Start"/"Stop").</summary>
    public volatile bool Enabled = true;
    /// <summary>While calibrating, the cursor is not moved and buttons go to <see cref="CalibrationButton"/>.</summary>
    public volatile bool Calibrating;
    public event Action<WiiButtons>? CalibrationButton;
    /// <summary>Short user-facing notice (e.g. trace started/saved); raised on the reader thread.</summary>
    public event Action<string>? Notice;

    public bool IrActive { get; private set; }
    public int VisibleDots => _tracker.VisibleDots;
    public double RollDegrees => _tracker.RollDegrees;
    public string MappingDescription => _mapping.Description;

    // Statistics, swapped every 5 s.
    private readonly Stopwatch _statsClock = Stopwatch.StartNew();
    private int _reports, _irReports, _moves;
    private double _procSumMs, _procMaxMs;
    public double ReportRate { get; private set; }
    public double CursorRate { get; private set; }
    public double IrPercent { get; private set; }
    public double ProcAvgMs { get; private set; }
    public double ProcMaxMs { get; private set; }
    private long _lastDebugLog;

    public PointerController(PointerConfig cfg, Rectangle screen)
    {
        _cfg = cfg;
        _filter = new OneEuroFilter2D(cfg.SmoothingMinCutoff, cfg.SmoothingBeta) { DCutoff = cfg.SmoothingDCutoff };
        _screen = screen;
        _mapping = PointerMapping.Create(cfg, screen);
    }

    public Rectangle Screen { get { lock (_lock) return _screen; } }

    public void SetScreen(Rectangle screen)
    {
        lock (_lock)
        {
            _screen = screen;
            _mapping = PointerMapping.Create(_cfg, screen);
            _filter.Reset();
        }
        Log.Info($"Pointer: target {screen}, mapping {_mapping.Description}");
    }

    public void FreezeFor(int ms) => _freezeUntil = Environment.TickCount64 + ms;

    /// <summary>Records the next N seconds of pointer samples to a CSV in the log folder.</summary>
    public void StartTrace(int seconds)
    {
        if (_trace != null) return;
        _trace = new PointerTrace(seconds);
        Notice?.Invoke($"Messung läuft – {seconds} s");
    }

    public void OnReport(WiiReport r)
    {
        bool hasAim = _tracker.TryGetAim(r.Dots, out double ax, out double ay);
        IrActive = hasAim;
        _reports++;

        if (hasAim)
        {
            _irReports++;
            lock (_aimHistory)
            {
                _aimHistory.Enqueue((r.Timestamp, ax, ay));
                while (_aimHistory.Count > 40) _aimHistory.Dequeue();
            }
        }

        if (hasAim && Enabled && !Calibrating)
        {
            Rectangle screen;
            PointerMapping mapping;
            lock (_lock) { screen = _screen; mapping = _mapping; }

            var (u, v) = mapping.Map(ax, ay);
            double px = screen.X + u * (screen.Width - 1);
            double py = screen.Y + v * (screen.Height - 1);
            var (fx, fy) = _filter.Filter(px, py, r.Timestamp / (double)Stopwatch.Frequency);

            // Soft dead zone: the cursor only follows once the filtered position leaves a small radius
            // around it, and then without a jump. Holding still gives a perfectly still cursor.
            double radius = _cfg.StillRadiusPercent / 100.0 * screen.Width;
            if (!_hadAim)
            {
                _filter.Reset(); // reacquired after a loss: jump straight to the new position
                (fx, fy) = _filter.Filter(px, py, r.Timestamp / (double)Stopwatch.Frequency);
                _stable = (fx, fy);
            }
            double sdx = fx - _stable.X, sdy = fy - _stable.Y, dist = Math.Sqrt(sdx * sdx + sdy * sdy);
            if (dist > radius)
            {
                double k = (dist - radius) / dist;
                _stable = (_stable.X + sdx * k, _stable.Y + sdy * k);
            }
            (fx, fy) = _stable;

            if (_trace != null && !_trace.Add(r.Timestamp, r.Dots.Count, ax, ay, _tracker.RollDegrees, px, py, fx, fy, (int)r.Buttons))
            {
                _trace = null;
                Notice?.Invoke("Messung gespeichert");
            }

            if (Environment.TickCount64 >= _freezeUntil)
            {
                var pos = ((int)Math.Round(fx), (int)Math.Round(fy));
                if (pos != _lastCursor)
                {
                    Input.MoveTo(pos.Item1, pos.Item2);
                    _lastCursor = pos;
                    _moves++;
                }
            }

            if (Log.DebugEnabled && Environment.TickCount64 - _lastDebugLog >= 200)
            {
                _lastDebugLog = Environment.TickCount64;
                Log.Debug($"IR {string.Join(" ", r.Dots.Select(d => $"({d.X},{d.Y})"))} roll={_tracker.RollDegrees:F0}° " +
                          $"aim=({ax:F0},{ay:F0}) screen=({px:F0},{py:F0}) filtered=({fx:F0},{fy:F0})");
            }
        }
        _hadAim = hasAim;

        if (Enabled || Calibrating) Router?.Process(r.Buttons, Calibrating ? CalibrationButton : null);

        double procMs = (Stopwatch.GetTimestamp() - r.Timestamp) * 1000.0 / Stopwatch.Frequency;
        _procSumMs += procMs;
        if (procMs > _procMaxMs) _procMaxMs = procMs;
        UpdateStats();
    }

    /// <summary>Wii disconnected: stop the pointer cleanly.</summary>
    public void OnSignalLost()
    {
        IrActive = false;
        _tracker.Reset();
        _hadAim = false;
    }

    public bool TryGetAverageAim(int windowMs, out double x, out double y)
    {
        x = y = 0;
        long now = Stopwatch.GetTimestamp(), window = Stopwatch.Frequency * windowMs / 1000;
        int n = 0;
        lock (_aimHistory)
        {
            foreach (var (t, ax, ay) in _aimHistory)
                if (now - t <= window) { x += ax; y += ay; n++; }
        }
        if (n < 3) return false;
        x /= n; y /= n;
        return true;
    }

    private void UpdateStats()
    {
        double s = _statsClock.Elapsed.TotalSeconds;
        if (s < 5) return;
        ReportRate = _reports / s;
        CursorRate = _moves / s;
        IrPercent = _reports == 0 ? 0 : 100.0 * _irReports / _reports;
        ProcAvgMs = _reports == 0 ? 0 : _procSumMs / _reports;
        ProcMaxMs = _procMaxMs;
        Log.Info($"Pointer stats: {ReportRate:F0} reports/s, IR visible {IrPercent:F0} %, cursor moves {CursorRate:F0}/s, " +
                 $"report->SendInput avg {ProcAvgMs:F2} ms max {ProcMaxMs:F2} ms, dots {VisibleDots}, roll {RollDegrees:F0}°");
        _reports = _irReports = _moves = 0;
        _procSumMs = _procMaxMs = 0;
        _statsClock.Restart();
    }
}
