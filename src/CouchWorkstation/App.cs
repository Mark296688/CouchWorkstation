using CouchWorkstation.Platform;
using CouchWorkstation.Pointer;
using CouchWorkstation.Speech;
using CouchWorkstation.Wii;
using Microsoft.Win32;

namespace CouchWorkstation;

/// <summary>Composition root: wires Wii input, pointer, buttons and speech together.</summary>
public sealed class App : IDisposable
{
    public AppConfig Config { get; }
    public WiiRemote Wii { get; }
    public PointerController Pointer { get; }
    public ButtonRouter Router { get; }
    public HandySpeechBackend Speech { get; }
    public DisplayInfo? Target { get; private set; }
    public string TargetStatus { get; private set; } = "";

    public event Action? CalibrationRequested;

    public App(AppConfig config)
    {
        Config = config;
        ResolveTarget();
        Speech = new HandySpeechBackend(config.Speech);
        Pointer = new PointerController(config.Pointer, Target?.Bounds ?? Rectangle.Empty);
        Router = new ButtonRouter(config, Pointer, Speech);
        Pointer.Router = Router;

        Wii = new WiiRemote();
        Wii.ReportReceived += Pointer.OnReport;
        Wii.ConnectionChanged += connected =>
        {
            if (connected)
            {
                if (Config.Pointer.Calibration == null) CalibrationRequested?.Invoke();
                return;
            }
            Router.ReleaseAll(); // e.g. B held while the remote dies -> recording is finalised
            Pointer.OnSignalLost();
        };

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void Start()
    {
        foreach (var d in Displays.GetAll())
            Log.Info($"Display: {d.DeviceName} \"{d.FriendlyName}\" bounds={d.Bounds} {d.RefreshHz:F2} Hz scale={d.ScalePercent}% primary={d.Primary}");
        Log.Info($"Target display: {TargetStatus}");
        Log.Info($"Pointer mapping: {Pointer.MappingDescription}");
        Wii.Start();
        // Starting Handy can take seconds (process start, AV scan) - never let it delay the pointer.
        Task.Run(() =>
        {
            try { Speech.Initialize(); }
            catch (Exception e) { Log.Error($"Speech init failed: {e}"); }
        });
    }

    public void SetTarget(DisplayInfo display)
    {
        Config.TargetDisplay = display.DeviceName;
        Config.TargetDisplayName = display.FriendlyName;
        Config.Save();
        ResolveTarget();
        Pointer.SetScreen(display.Bounds);
    }

    public void SetPointerEnabled(bool enabled)
    {
        Pointer.Enabled = enabled;
        if (!enabled) Router.ReleaseAll();
        Log.Info($"Pointer {(enabled ? "started" : "stopped")}");
    }

    public void SaveCalibration(PointerCalibration cal)
    {
        Config.Pointer.Calibration = cal;
        Config.Save();
        Pointer.SetScreen(Target?.Bounds ?? Pointer.Screen);
        Log.Info($"Calibration saved: centre=({cal.CenterX:F0},{cal.CenterY:F0}) scale=({cal.ScaleX:F5},{cal.ScaleY:F5}); {Pointer.MappingDescription}");
    }

    private void ResolveTarget()
    {
        Target = Displays.Resolve(Config.TargetDisplay, Config.TargetDisplayName, out bool matched);
        if (Target == null) TargetStatus = "kein Display gefunden";
        else if (matched || Config.TargetDisplay == null) TargetStatus = $"{Target.FriendlyName} ({Target.DeviceName}) {Target.Bounds}";
        else TargetStatus = $"konfiguriert \"{Config.TargetDisplayName}\" nicht verfügbar – verwende {Target.FriendlyName}";
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        ResolveTarget();
        Log.Info($"Display settings changed; target now {TargetStatus}");
        if (Target != null) Pointer.SetScreen(Target.Bounds);
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Pointer.Enabled = false;
        Router.ReleaseAll();
        Wii.Dispose();
        Speech.Dispose();
    }
}
