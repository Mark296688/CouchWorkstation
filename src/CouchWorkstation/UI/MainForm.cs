using System.Diagnostics;
using CouchWorkstation.Platform;
using CouchWorkstation.Speech;

namespace CouchWorkstation.UI;

/// <summary>Small status window. Closing it exits the application.</summary>
public sealed class MainForm : Form
{
    private readonly App _app;
    private readonly Label _wii = new(), _ir = new(), _rate = new(), _handy = new(), _speech = new(), _latency = new(), _mapping = new();
    private readonly ComboBox _display = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
    private readonly Button _toggle = new() { Text = "Stop", AutoSize = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private CalibrationForm? _calibration;
    private bool _fillingDisplays;

    public MainForm(App app)
    {
        _app = app;
        Text = "Couch Workstation";
        Font = new Font("Segoe UI", 10f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(12), Dock = DockStyle.Fill };
        void Row(string caption, Control value)
        {
            grid.Controls.Add(new Label { Text = caption, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 6, 12, 6) });
            value.Margin = new Padding(3, 6, 3, 6);
            if (value is Label l) l.AutoSize = true;
            grid.Controls.Add(value);
        }
        Row("Wii Remote", _wii);
        Row("IR-Pointer", _ir);
        Row("Update-Rate", _rate);
        Row("Ziel-Display", _display);
        Row("Abbildung", _mapping);
        Row("Handy", _handy);
        Row("Sprache", _speech);
        Row("Letztes Diktat", _latency);

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        var calibrate = new Button { Text = "Kalibrieren", AutoSize = true };
        var logs = new Button { Text = "Log-Ordner", AutoSize = true };
        var config = new Button { Text = "Konfiguration", AutoSize = true };
        var exit = new Button { Text = "Beenden", AutoSize = true };
        buttons.Controls.AddRange([_toggle, calibrate, logs, config, exit]);
        grid.Controls.Add(buttons);
        grid.SetColumnSpan(buttons, 2);
        Controls.Add(grid);

        _toggle.Click += (_, _) =>
        {
            _app.SetPointerEnabled(!_app.Pointer.Enabled);
            _toggle.Text = _app.Pointer.Enabled ? "Stop" : "Start";
        };
        calibrate.Click += (_, _) => StartCalibration();
        logs.Click += (_, _) => Process.Start("explorer.exe", Log.Directory);
        config.Click += (_, _) => Process.Start("explorer.exe", $"/select,\"{AppConfig.FilePath}\"");
        exit.Click += (_, _) => Close();
        _display.SelectedIndexChanged += (_, _) =>
        {
            if (!_fillingDisplays && _display.SelectedItem is DisplayInfo d) _app.SetTarget(d);
        };
        _app.CalibrationRequested += () => { if (IsHandleCreated) BeginInvoke(StartCalibration); };
        _app.Pointer.Notice += text =>
        {
            if (IsHandleCreated && _app.Target != null) BeginInvoke(() => Toast.ShowOn(_app.Target.Bounds, text));
        };

        FillDisplays();
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();
        RefreshStatus();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _app.Start();
    }

    private void FillDisplays()
    {
        _fillingDisplays = true;
        _display.Items.Clear();
        foreach (var d in Displays.GetAll()) _display.Items.Add(d);
        _display.SelectedItem = _display.Items.Cast<DisplayInfo>().FirstOrDefault(d => d.DeviceName == _app.Target?.DeviceName);
        _fillingDisplays = false;
    }

    private void StartCalibration()
    {
        if (_calibration is { IsDisposed: false }) { _calibration.Activate(); return; }
        if (_app.Target == null) return;
        _calibration = new CalibrationForm(_app);
        _calibration.Show();
    }

    private void RefreshStatus()
    {
        var wii = _app.Wii;
        var p = _app.Pointer;
        _wii.Text = wii.Connected ? $"Verbunden · {wii.Slot} · Akku {wii.BatteryPercent} %" : "Nicht verbunden – Wii Remote einschalten (Taste drücken)";
        _wii.ForeColor = wii.Connected ? Color.DarkGreen : Color.DarkRed;
        _ir.Text = !wii.Connected ? "–" : p.IrActive ? $"Aktiv · {p.VisibleDots} Punkt(e) · Neigung {p.RollDegrees:F0}°{(p.Enabled ? "" : " · gestoppt")}" : "Kein Signal";
        _ir.ForeColor = p.IrActive ? Color.DarkGreen : Color.DarkOrange;
        _rate.Text = wii.Connected
            ? $"{p.ReportRate:F0} Reports/s · {p.CursorRate:F0} Cursor-Updates/s · Verarbeitung Ø {p.ProcAvgMs:F2} ms"
            : "–";
        _mapping.Text = (_app.Config.Pointer.Calibration == null ? "Nicht kalibriert · " : "") + _app.TargetStatus;

        _app.Speech.RefreshStatus();
        _handy.Text = _app.Speech.StatusText;
        _handy.ForeColor = _app.Speech.State == SpeechState.Unavailable ? Color.DarkRed : Color.DarkGreen;
        _speech.Text = _app.Speech.State switch
        {
            SpeechState.Listening => "Hört zu (B gedrückt)",
            SpeechState.Processing => "Verarbeitet …",
            SpeechState.Ready => "Bereit",
            _ => "Nicht verfügbar",
        };
        _latency.Text = _app.Speech.LastLatencyText;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();
        _calibration?.Close();
        base.OnFormClosing(e);
    }
}
