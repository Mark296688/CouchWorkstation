namespace CouchWorkstation.Pointer;

/// <summary>
/// 2D One-Euro filter (Casiez et al. 2012): strong smoothing while the pointer is almost still
/// (no jitter), smoothing fades out with speed (no visible lag on fast movements).
/// Cutoff is driven by the 2D speed so both axes behave consistently.
/// </summary>
public sealed class OneEuroFilter2D
{
    public double MinCutoff { get; set; }
    public double Beta { get; set; }
    public double DCutoff { get; set; } = 1.0;

    private double _x, _y, _speed, _t;
    private bool _init;

    public OneEuroFilter2D(double minCutoff, double beta)
    {
        MinCutoff = minCutoff;
        Beta = beta;
    }

    public void Reset() => _init = false;

    public (double X, double Y) Filter(double x, double y, double t)
    {
        if (!_init)
        {
            _init = true;
            _x = x; _y = y; _speed = 0; _t = t;
            return (x, y);
        }
        double dt = t - _t;
        if (dt <= 0) dt = 1e-3;
        _t = t;

        double speed = Math.Sqrt((x - _x) * (x - _x) + (y - _y) * (y - _y)) / dt;
        _speed += Alpha(DCutoff, dt) * (speed - _speed);
        double a = Alpha(MinCutoff + Beta * _speed, dt);
        _x += a * (x - _x);
        _y += a * (y - _y);
        return (_x, _y);
    }

    private static double Alpha(double cutoff, double dt)
    {
        double tau = 1.0 / (2 * Math.PI * cutoff);
        return 1.0 / (1.0 + tau / dt);
    }
}
