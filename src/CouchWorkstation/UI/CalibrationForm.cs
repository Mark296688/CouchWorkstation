using CouchWorkstation.Pointer;
using CouchWorkstation.Wii;

namespace CouchWorkstation.UI;

/// <summary>
/// Full-screen calibration on the target display: aim at the centre (A), then at the lower-left
/// target (A). The lower-left target is used because the camera keeps seeing the DolphinBar there
/// (it is mounted below the TV); upper targets would push the bar out of the camera image.
/// Home on the remote or Esc cancels.
/// </summary>
public sealed class CalibrationForm : Form
{
    private static readonly (double U, double V)[] Targets = [(0.5, 0.5), (0.15, 0.85)];
    private static readonly string[] Prompts =
    [
        "Zeige ganz natürlich auf das Kreuz in der Mitte und drücke A",
        "Zeige jetzt auf das Kreuz unten links und drücke A",
    ];

    private readonly App _app;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 50 };
    private int _step;
    private (double X, double Y) _center;
    private string _message = "";
    private bool _done;

    public CalibrationForm(App app)
    {
        _app = app;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = app.Target!.Bounds;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        DoubleBuffered = true;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        _timer.Tick += (_, _) => Invalidate();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _app.Pointer.Calibrating = true;
        _app.Pointer.CalibrationButton += OnButton;
        _timer.Start();
        Log.Info("Calibration started");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _app.Pointer.CalibrationButton -= OnButton;
        _app.Pointer.Calibrating = false;
        if (!_done) Log.Info("Calibration cancelled");
        base.OnFormClosed(e);
    }

    private void OnButton(WiiButtons b) => BeginInvoke(() => HandleButton(b));

    private void HandleButton(WiiButtons b)
    {
        if (_done) return;
        if (b == WiiButtons.Home) { Close(); return; }
        if (b != WiiButtons.A) return;

        if (!_app.Pointer.TryGetAverageAim(150, out double x, out double y))
        {
            _message = "Kein IR-Signal – etwas tiefer zielen (Richtung DolphinBar) und nochmal A";
            Log.Info($"Calibration step {_step + 1}: no IR");
            return;
        }
        Log.Info($"Calibration step {_step + 1}: aim=({x:F1},{y:F1}) roll={_app.Pointer.RollDegrees:F0}°");
        if (_step == 0)
        {
            _center = (x, y);
            _step = 1;
            _message = "";
            return;
        }

        var cal = PointerMapping.Calibrate(_center, (x, y), Targets[1], Bounds, _app.Config.Pointer);
        _app.SaveCalibration(cal);
        _done = true;
        _message = "Fertig!";
        Invalidate();
        var close = new System.Windows.Forms.Timer { Interval = 900 };
        close.Tick += (_, _) => { close.Dispose(); Close(); };
        close.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        using var big = new Font("Segoe UI", 28f, FontStyle.Bold);
        using var small = new Font("Segoe UI", 18f);
        var center = new StringFormat { Alignment = StringAlignment.Center };

        var (u, v) = Targets[_done ? 1 : _step];
        float tx = (float)(u * (Width - 1)), ty = (float)(v * (Height - 1));
        float r = 40 * scale;
        using (var pen = new Pen(_done ? Color.LimeGreen : Color.White, 4 * scale))
        {
            g.DrawEllipse(pen, tx - r, ty - r, 2 * r, 2 * r);
            g.DrawLine(pen, tx - 1.6f * r, ty, tx + 1.6f * r, ty);
            g.DrawLine(pen, tx, ty - 1.6f * r, tx, ty + 1.6f * r);
        }

        var p = _app.Pointer;
        string ir = !_app.Wii.Connected ? "Wii Remote nicht verbunden"
            : p.IrActive ? $"IR: {p.VisibleDots} Punkt(e) sichtbar" : "IR: kein Signal";
        string text = _done ? "Kalibrierung gespeichert" : $"Schritt {_step + 1}/2: {Prompts[_step]}";
        g.DrawString(text, big, Brushes.White, new RectangleF(0, Height * 0.08f, Width, Height * 0.2f), center);
        g.DrawString(ir, small, p.IrActive ? Brushes.LimeGreen : Brushes.OrangeRed, new RectangleF(0, Height * 0.2f, Width, Height * 0.1f), center);
        g.DrawString(_message, small, Brushes.Gold, new RectangleF(0, Height * 0.27f, Width, Height * 0.1f), center);
        g.DrawString("Home oder Esc = Abbrechen", small, Brushes.Gray, new RectangleF(0, Height * 0.93f, Width, Height * 0.07f), center);
    }
}
