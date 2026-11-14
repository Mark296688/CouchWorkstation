using System.Diagnostics;
using System.Text.Json;
using CouchWorkstation.Platform;

namespace CouchWorkstation.Speech;

public enum SpeechState { Unavailable, Ready, Listening, Processing }

public interface ISpeechBackend : IDisposable
{
    string Name { get; }
    SpeechState State { get; }
    string StatusText { get; }
    string LastLatencyText { get; }
    void Initialize();
    void Begin();
    void End();
}

/// <summary>
/// Uses the locally installed "Handy" (github.com/cjpais/Handy) for speech-to-text. Handy already does
/// microphone capture, local transcription and pasting into the focused window, so this backend only
/// triggers it: while B is held, Handy's own push-to-talk shortcut is held (read from Handy's settings),
/// or alternatively "handy.exe --toggle-transcription" is invoked on press and release.
/// </summary>
public sealed class HandySpeechBackend : ISpeechBackend
{
    private readonly SpeechConfig _cfg;
    private readonly string _exe;
    private readonly string _settingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "com.pais.handy", "settings_store.json");

    private List<Input.Key>? _hotkey;
    private string _hotkeyText = "?";
    private bool _pushToTalk = true;
    private bool _useCli;
    private bool _keysHeld;
    private volatile bool _running;
    private long _beginTicks, _endTicks, _readyMs = -1;
    private readonly HandyLogWatcher _watcher = new();

    public string Name => "Handy";
    public SpeechState State { get; private set; } = SpeechState.Unavailable;
    public string StatusText { get; private set; } = "nicht initialisiert";
    public string LastLatencyText { get; private set; } = "–";

    public HandySpeechBackend(SpeechConfig cfg)
    {
        _cfg = cfg;
        _exe = cfg.HandyExePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Handy", "handy.exe");
    }

    public static bool IsHandyRunning() => Process.GetProcessesByName("handy").Length > 0;

    public void Initialize()
    {
        if (!File.Exists(_exe))
        {
            State = SpeechState.Unavailable;
            StatusText = "Handy nicht gefunden";
            Log.Error($"Handy: executable not found at {_exe}");
            return;
        }
        ReadHandySettings();
        _useCli = _cfg.HandyTrigger.Equals("Cli", StringComparison.OrdinalIgnoreCase) || _hotkey == null;
        ReleaseStuckHotkey();

        if (!IsHandyRunning() && _cfg.StartHandyIfNotRunning)
        {
            try
            {
                Process.Start(new ProcessStartInfo(_exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(_exe)! });
                Log.Info($"Handy: was not running, started {_exe}");
            }
            catch (Exception e) { Log.Error($"Handy: could not start: {e.Message}"); }
        }
        RefreshStatus();
        _watcher.Event += OnHandyEvent;
        _watcher.Start();
        Log.Info($"Handy: trigger={(_useCli ? "CLI --toggle-transcription" : $"hotkey {_hotkeyText}")}, push_to_talk={_pushToTalk}, exe={_exe}");
    }

    public void RefreshStatus()
    {
        bool running = _running = IsHandyRunning();
        if (State == SpeechState.Processing && Elapsed(_endTicks) > 15000)
        {
            Log.Warn("Speech: no paste reported by Handy within 15 s after B UP");
            State = SpeechState.Ready;
        }
        if (State is SpeechState.Listening or SpeechState.Processing) return;
        State = running ? SpeechState.Ready : SpeechState.Unavailable;
        StatusText = running
            ? $"läuft · {(_useCli ? "CLI-Trigger" : _hotkeyText)}{(_pushToTalk || _useCli ? "" : " (Toggle)")}"
            : "läuft nicht";
    }

    public void Begin()
    {
        if (State == SpeechState.Listening) return;
        if (!_running)
        {
            Log.Warn("Speech: B DOWN but Handy is not running - nothing is sent");
            StatusText = "läuft nicht";
            return;
        }
        _beginTicks = Stopwatch.GetTimestamp();
        _readyMs = -1;
        State = SpeechState.Listening;
        Log.Info("Speech: B DOWN -> Handy start");
        Trigger(start: true);
    }

    public void End()
    {
        if (State != SpeechState.Listening) return;
        _endTicks = Stopwatch.GetTimestamp();
        Trigger(start: false);
        State = SpeechState.Processing;
        Log.Info($"Speech: B UP -> Handy finalise (held {Elapsed(_beginTicks, _endTicks):F0} ms)");
    }

    private static double Elapsed(long from, long? to = null) =>
        ((to ?? Stopwatch.GetTimestamp()) - from) * 1000.0 / Stopwatch.Frequency;

    private void OnHandyEvent(HandyLogWatcher.Kind kind, long ts, string line)
    {
        switch (kind)
        {
            case HandyLogWatcher.Kind.HotkeyPressed:
                Log.Info($"Speech: Handy saw push-to-talk press (+{Elapsed(_beginTicks, ts):F0} ms after B DOWN)");
                break;
            case HandyLogWatcher.Kind.RecordingReady:
                _readyMs = (long)Elapsed(_beginTicks, ts);
                Log.Info($"Speech: Handy microphone ready (+{_readyMs} ms after B DOWN)");
                break;
            case HandyLogWatcher.Kind.HotkeyReleased:
                Log.Info($"Speech: Handy saw push-to-talk release (+{Elapsed(_endTicks, ts):F0} ms after B UP)");
                break;
            case HandyLogWatcher.Kind.Pasted:
                if (State != SpeechState.Processing) break;
                double total = Elapsed(_endTicks, ts);
                LastLatencyText = $"B loslassen → Text eingefügt: {total:F0} ms" + (_readyMs >= 0 ? $" · Mikro bereit nach {_readyMs} ms" : "");
                Log.Info($"Speech: TEXT INSERTED, B UP -> pasted {total:F0} ms (Handy: {line[(line.IndexOf("Text pasted", StringComparison.Ordinal))..]})");
                State = SpeechState.Ready;
                break;
            case HandyLogWatcher.Kind.NoAudio:
                if (State != SpeechState.Processing) break;
                LastLatencyText = "Keine Sprache erkannt – nichts eingefügt";
                Log.Warn($"Speech: Handy recorded no usable audio, nothing inserted ({line})");
                State = SpeechState.Ready;
                break;
            case HandyLogWatcher.Kind.Error:
                Log.Warn($"Speech: Handy error: {line}");
                break;
        }
    }

    private void Trigger(bool start)
    {
        if (_useCli)
        {
            // Toggle semantics: one call starts, the next one stops. Run off the reader thread.
            Task.Run(() =>
            {
                try { Process.Start(new ProcessStartInfo(_exe, "--toggle-transcription") { UseShellExecute = false, CreateNoWindow = true }); }
                catch (Exception e) { Log.Error($"Handy CLI trigger failed: {e.Message}"); }
            });
            return;
        }

        if (_pushToTalk)
        {
            // Handy records while its shortcut is held: press on B down, release on B up.
            Input.Keys(_hotkey!, down: start);
            _keysHeld = start;
        }
        else
        {
            // Handy is configured as toggle: tap the shortcut on both edges.
            Input.Keys(_hotkey!, down: true);
            Input.Keys(_hotkey!, down: false);
        }
    }

    /// <summary>
    /// If a previous run was killed while B was held, the injected modifier stays logically pressed
    /// (every click becomes Ctrl+click). Release it once at startup.
    /// </summary>
    private void ReleaseStuckHotkey()
    {
        if (_hotkey == null || !_hotkey.Any(k => Input.IsKeyDown(k.Vk))) return;
        Input.Keys(_hotkey, down: false);
        Log.Warn($"Handy: hotkey {_hotkeyText} was still held down (previous run killed?) - released");
    }

    private void ReadHandySettings()
    {
        string? binding = _cfg.HandyHotkeyOverride;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_settingsPath));
            var s = doc.RootElement.GetProperty("settings");
            if (s.TryGetProperty("push_to_talk", out var ptt)) _pushToTalk = ptt.GetBoolean();
            binding ??= s.GetProperty("bindings").GetProperty("transcribe").GetProperty("current_binding").GetString();
        }
        catch (Exception e) { Log.Warn($"Handy: settings not readable ({_settingsPath}): {e.Message}"); }

        _hotkeyText = binding ?? "?";
        _hotkey = binding != null ? ParseHotkey(binding) : null;
        if (_hotkey == null) Log.Warn($"Handy: hotkey '{binding}' not understood, falling back to CLI trigger");
    }

    /// <summary>Parses Handy's shortcut notation, e.g. "ctrl+space", "ctrl+shift+f9".</summary>
    public static List<Input.Key>? ParseHotkey(string binding)
    {
        var keys = new List<Input.Key>();
        foreach (var raw in binding.ToLowerInvariant().Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            // Left-hand modifier VKs, as a physical keyboard delivers them to low-level hooks.
            Input.Key? k = raw switch
            {
                "ctrl" or "control" => new Input.Key(0xA2, 0x1D, false),
                "shift" => new Input.Key(0xA0, 0x2A, false),
                "alt" or "option" => new Input.Key(0xA4, 0x38, false),
                "super" or "win" or "meta" or "command" or "cmd" => new Input.Key(0x5B, 0x5B, true),
                "space" => new Input.Key(0x20, 0x39, false),
                "enter" or "return" => new Input.Key(0x0D, 0x1C, false),
                "tab" => new Input.Key(0x09, 0x0F, false),
                // Numpad + and - (browsers report the key as "+" / "-"); '+' itself is the separator
                "plus" => new Input.Key(0x6B, 0x4E, false),
                "minus" => new Input.Key(0x6D, 0x4A, false),
                "escape" or "esc" => new Input.Key(0x1B, 0x01, false),
                _ when raw.Length == 1 && char.IsAsciiLetterOrDigit(raw[0]) => Vk(char.ToUpperInvariant(raw[0])),
                _ when raw.Length is 2 or 3 && raw[0] == 'f' && int.TryParse(raw[1..], out var n) && n is >= 1 and <= 24 => Vk(0x70 + n - 1),
                _ => null,
            };
            if (k == null) return null;
            keys.Add(k.Value);
        }
        return keys.Count > 0 ? keys : null;

        static Input.Key Vk(int vk) => new((ushort)vk, Input.ScanCode((ushort)vk), false);
    }

    public void Dispose()
    {
        _watcher.Dispose();
        // Never leave a modifier stuck down.
        if (_keysHeld && _hotkey != null)
        {
            Input.Keys(_hotkey, down: false);
            _keysHeld = false;
        }
    }
}
