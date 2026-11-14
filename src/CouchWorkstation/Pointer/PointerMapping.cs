namespace CouchWorkstation.Pointer;

/// <summary>
/// Linear mapping from the roll-corrected camera aim point to normalised screen coordinates (0..1).
///
/// The calibration measures the user's natural aim (centre + one corner), i.e. a 1:1 mapping
/// between where the remote points and where the cursor appears. That mapping is then made
/// feasible: the camera only sees the DolphinBar within its field of view, so if a screen edge
/// would need the bar outside the image (typically the top edge, with the bar below a large TV),
/// the mapping is first shifted and, if that is not enough, its range is compressed.
/// </summary>
public sealed class PointerMapping
{
    public const double CamW = 1024, CamH = 768;

    public double CenterX { get; }
    public double CenterY { get; }
    public double ScaleX { get; }
    public double ScaleY { get; }
    public string Description { get; }

    private PointerMapping(double cx, double cy, double sx, double sy, string description)
    {
        CenterX = cx; CenterY = cy; ScaleX = sx; ScaleY = sy;
        Description = description;
    }

    public (double U, double V) Map(double ax, double ay) =>
        (Math.Clamp(0.5 + (ax - CenterX) * ScaleX, 0, 1),
         Math.Clamp(0.5 + (ay - CenterY) * ScaleY, 0, 1));

    public static PointerMapping Create(PointerConfig cfg, Rectangle screen)
    {
        double cx, cy, sx, sy;
        string source;
        if (cfg.Calibration is { } cal)
        {
            (cx, cy, sx, sy) = (cal.CenterX, cal.CenterY, cal.ScaleX, cal.ScaleY);
            source = $"calibrated {cal.Created:yyyy-MM-dd HH:mm}";
        }
        else
        {
            // Same angular gain on both axes: screen pixels per camera pixel.
            double k = screen.Width / (2 * cfg.DefaultHalfRangeX);
            cx = CamW / 2; cy = CamH / 2;
            sx = (cfg.InvertX ? -1 : 1) * k / screen.Width;
            sy = (cfg.InvertY ? -1 : 1) * k / screen.Height;
            source = "uncalibrated defaults";
        }

        var (fx, fsx, notesX) = Fit(cx, sx, CamW, cfg.CameraMargin, "X");
        var (fy, fsy, notesY) = Fit(cy, sy, CamH, cfg.CameraMargin, "Y");
        string desc = $"{source}: centre=({fx:F0},{fy:F0}) half-range=({0.5 / Math.Abs(fsx):F0},{0.5 / Math.Abs(fsy):F0}) cam px, " +
                      $"signs=({Math.Sign(fsx)},{Math.Sign(fsy)}){notesX}{notesY}";
        return new PointerMapping(fx, fy, fsx, fsy, desc);
    }

    /// <summary>Keeps centre ± half-range inside the usable camera window.</summary>
    private static (double Center, double Scale, string Notes) Fit(double center, double scale, double size, double margin, string axis)
    {
        string notes = "";
        double lo = margin, hi = size - 1 - margin;
        double half = 0.5 / Math.Abs(scale);
        if (2 * half > hi - lo)
        {
            half = (hi - lo) / 2;
            scale = Math.Sign(scale) * 0.5 / half;
            notes += $", {axis} range compressed";
        }
        double fitted = Math.Clamp(center, lo + half, hi - half);
        if (Math.Abs(fitted - center) > 0.5) notes += $", {axis} centre shifted by {fitted - center:F0} cam px";
        return (fitted, scale, notes);
    }

    /// <summary>
    /// Builds a calibration from the aim at the screen centre and the aim at a second target (u,v).
    /// An axis where the user barely moved falls back to the other axis' gain (angular gain is isotropic).
    /// </summary>
    public static PointerCalibration Calibrate((double X, double Y) center, (double X, double Y) target,
        (double U, double V) targetPos, Rectangle screen, PointerConfig cfg)
    {
        const double minDelta = 25;
        double dx = target.X - center.X, dy = target.Y - center.Y;
        double? sx = Math.Abs(dx) >= minDelta ? (targetPos.U - 0.5) / dx : null;
        double? sy = Math.Abs(dy) >= minDelta ? (targetPos.V - 0.5) / dy : null;

        // Screen pixels per camera pixel for each measured axis.
        double kDefault = screen.Width / (2 * cfg.DefaultHalfRangeX);
        double? kx = sx.HasValue ? Math.Abs(sx.Value) * screen.Width : null;
        double? ky = sy.HasValue ? Math.Abs(sy.Value) * screen.Height : null;
        double kFallback = kx ?? ky ?? kDefault;

        double scaleX = sx ?? (cfg.InvertX ? -1 : 1) * kFallback / screen.Width;
        double scaleY = sy ?? (cfg.InvertY ? -1 : 1) * kFallback / screen.Height;

        return new PointerCalibration
        {
            CenterX = center.X,
            CenterY = center.Y,
            ScaleX = scaleX,
            ScaleY = scaleY,
            Created = DateTime.Now,
        };
    }
}
