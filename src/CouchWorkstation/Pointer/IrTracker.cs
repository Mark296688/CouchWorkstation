using CouchWorkstation.Wii;

namespace CouchWorkstation.Pointer;

/// <summary>
/// Turns the raw IR dots of the two DolphinBar LED clusters into one aim point in camera
/// coordinates: the midpoint of both clusters, rotated back by the remote's roll angle.
/// When one cluster leaves the camera's field of view, the last known cluster offset is used,
/// which extends the usable range at the image border. After a full loss, tracking only resumes
/// with both clusters visible, so a wrongly assigned single dot can never make the cursor jump.
/// </summary>
public sealed class IrTracker
{
    private const double Cx = 511.5, Cy = 383.5;

    /// <summary>Per-report blend factor of the roll low-pass (~100 reports/s -> ~50 ms time constant).</summary>
    private const double RollSmoothing = 0.2;

    private (double X, double Y) _left, _right;
    private bool _tracking;
    private double _roll = double.NaN;

    public int VisibleDots { get; private set; }
    public double RollDegrees { get; private set; }
    public double Spacing { get; private set; }

    public bool TryGetAim(IReadOnlyList<IrDot> dots, out double ax, out double ay)
    {
        VisibleDots = dots.Count;
        ax = ay = 0;

        if (dots.Count >= 2)
        {
            var (a, b) = PickPair(dots);
            if (a.X > b.X) (a, b) = (b, a);
            _left = (a.X, a.Y);
            _right = (b.X, b.Y);
            _tracking = true;
        }
        else if (dots.Count == 1 && _tracking)
        {
            var d = (X: (double)dots[0].X, Y: (double)dots[0].Y);
            var off = (X: _right.X - _left.X, Y: _right.Y - _left.Y);
            if (Dist2(d, _left) <= Dist2(d, _right))
            {
                _left = d;
                _right = (d.X + off.X, d.Y + off.Y);
            }
            else
            {
                _right = d;
                _left = (d.X - off.X, d.Y - off.Y);
            }
        }
        else
        {
            _tracking = false;
            _roll = double.NaN;
            return false;
        }

        double dx = _right.X - _left.X, dy = _right.Y - _left.Y;
        // The dots are only ~100 px apart, so one pixel of noise is ~0.6° of roll, which the rotation below
        // turns into several pixels of aim jitter. Roll changes slowly, so it is low-passed on its own.
        double rawRoll = Math.Atan2(dy, dx);
        _roll = double.IsNaN(_roll) ? rawRoll : _roll + RollSmoothing * Math.IEEERemainder(rawRoll - _roll, 2 * Math.PI);
        double roll = _roll;
        RollDegrees = roll * 180 / Math.PI;
        Spacing = Math.Sqrt(dx * dx + dy * dy);

        // Rotate the midpoint around the optical centre so the bar becomes horizontal again.
        double mx = (_left.X + _right.X) / 2 - Cx, my = (_left.Y + _right.Y) / 2 - Cy;
        double c = Math.Cos(-roll), s = Math.Sin(-roll);
        ax = Cx + mx * c - my * s;
        ay = Cy + mx * s + my * c;
        return true;
    }

    public void Reset()
    {
        _tracking = false;
        _roll = double.NaN;
    }

    private (IrDot, IrDot) PickPair(IReadOnlyList<IrDot> dots)
    {
        if (dots.Count == 2) return (dots[0], dots[1]);
        // More than two blobs (reflections, lamps): prefer the pair that best matches the last known bar.
        double best = double.MaxValue;
        (IrDot, IrDot) pair = (dots[0], dots[1]);
        var lastMid = ((_left.X + _right.X) / 2, (_left.Y + _right.Y) / 2);
        double lastSpacing = Math.Sqrt(Dist2(_left, _right));
        for (int i = 0; i < dots.Count; i++)
            for (int j = i + 1; j < dots.Count; j++)
            {
                double score;
                if (_tracking)
                {
                    double sp = Math.Sqrt(Dist2((dots[i].X, dots[i].Y), (dots[j].X, dots[j].Y)));
                    var mid = ((dots[i].X + dots[j].X) / 2.0, (dots[i].Y + dots[j].Y) / 2.0);
                    score = Math.Abs(sp - lastSpacing) + 0.5 * Math.Sqrt(Dist2(mid, lastMid));
                }
                else score = -(dots[i].Size + dots[j].Size);
                if (score < best) { best = score; pair = (dots[i], dots[j]); }
            }
        return pair;
    }

    private static double Dist2((double X, double Y) a, (double X, double Y) b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
}
