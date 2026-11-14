using System.Text.Json;
using System.Text.Json.Serialization;

namespace CouchWorkstation;

/// <summary>User configuration, stored as JSON in %APPDATA%\CouchWorkstation\config.json.</summary>
public sealed class AppConfig
{
    /// <summary>GDI device name of the target display, e.g. \\.\DISPLAY2.</summary>
    public string? TargetDisplay { get; set; }
    /// <summary>Monitor name from EDID (e.g. "Mi TV"); preferred over the device name when matching.</summary>
    public string? TargetDisplayName { get; set; }

    public PointerConfig Pointer { get; set; } = new();

    /// <summary>Wii button name -> action name. Actions: LeftClick, PushToTalk, RecordPointerTrace, Key:&lt;keys&gt;, None.</summary>
    public Dictionary<string, string> Buttons { get; set; } = new()
    {
        ["A"] = "LeftClick",
        ["B"] = "PushToTalk",
        ["One"] = "RecordPointerTrace",
        ["Plus"] = "Key:plus",
        ["Minus"] = "Key:minus",
    };

    public SpeechConfig Speech { get; set; } = new();

    public bool DebugLogging { get; set; }

    [JsonIgnore]
    public static string FilePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CouchWorkstation", "config.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Json) ?? new AppConfig();
        }
        catch (Exception e)
        {
            var bad = FilePath + ".invalid";
            File.Copy(FilePath, bad, overwrite: true);
            Log.Warn($"Config could not be read ({e.Message}); using defaults, broken file kept as {bad}");
        }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception e) { Log.Error($"Config could not be saved: {e.Message}"); }
    }
}

public sealed class PointerConfig
{
    /// <summary>Result of the in-app calibration; null = uncalibrated defaults.</summary>
    public PointerCalibration? Calibration { get; set; }

    /// <summary>Uncalibrated: camera pixels from the centre to the left/right screen edge.</summary>
    public double DefaultHalfRangeX { get; set; } = 330;
    /// <summary>Uncalibrated axis directions (the calibration determines them automatically).</summary>
    public bool InvertX { get; set; }
    public bool InvertY { get; set; }

    /// <summary>The bar midpoint is kept at least this far (camera px) from the camera image border.</summary>
    public double CameraMargin { get; set; } = 30;

    /// <summary>One-Euro filter: lower MinCutoff = calmer when still; higher Beta = less lag when moving fast.</summary>
    public double SmoothingMinCutoff { get; set; } = 1.0;
    public double SmoothingBeta { get; set; } = 0.04;
    public double SmoothingDCutoff { get; set; } = 2.0;

    /// <summary>Soft dead zone (percent of screen width): the cursor stays still until the hand moves further than this.</summary>
    public double StillRadiusPercent { get; set; } = 0.4;

    /// <summary>The cursor holds still this long after A is pressed, so the press itself does not move the click.</summary>
    public int ClickFreezeMs { get; set; } = 150;
}

public sealed class PointerCalibration
{
    /// <summary>Roll-corrected camera position of the bar while aiming at the screen centre.</summary>
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    /// <summary>Normalised screen units (0..1) per camera pixel, signed.</summary>
    public double ScaleX { get; set; }
    public double ScaleY { get; set; }
    public DateTime Created { get; set; }
}

public sealed class SpeechConfig
{
    /// <summary>Speech backend: "Handy".</summary>
    public string Backend { get; set; } = "Handy";
    /// <summary>null = %LOCALAPPDATA%\Handy\handy.exe</summary>
    public string? HandyExePath { get; set; }
    /// <summary>"Hotkey": hold Handy's push-to-talk shortcut while B is held. "Cli": handy.exe --toggle-transcription.</summary>
    public string HandyTrigger { get; set; } = "Hotkey";
    /// <summary>null = read Handy's own transcribe shortcut from its settings.</summary>
    public string? HandyHotkeyOverride { get; set; }
    public bool StartHandyIfNotRunning { get; set; } = true;
}
