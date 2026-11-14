using CouchWorkstation.Platform;
using CouchWorkstation.Speech;
using CouchWorkstation.Wii;

namespace CouchWorkstation.Pointer;

public interface IButtonAction
{
    string Name { get; }
    void Down();
    void Up();
}

/// <summary>Maps Wii button edges to actions according to the config (e.g. "A": "LeftClick").</summary>
public sealed class ButtonRouter
{
    private readonly Dictionary<WiiButtons, IButtonAction> _map = new();
    private readonly HashSet<WiiButtons> _active = new();
    private WiiButtons _prev;

    public WiiButtons Current => _prev;

    public ButtonRouter(AppConfig cfg, PointerController pointer, ISpeechBackend speech)
    {
        foreach (var (button, action) in cfg.Buttons)
        {
            if (!Enum.TryParse<WiiButtons>(button, true, out var b) || b == WiiButtons.None)
            {
                Log.Warn($"Config: unknown Wii button '{button}'");
                continue;
            }
            IButtonAction? a = action switch
            {
                "LeftClick" => new LeftClickAction(pointer, cfg.Pointer.ClickFreezeMs),
                "PushToTalk" => new PushToTalkAction(speech),
                "RecordPointerTrace" => new RecordTraceAction(pointer),
                "None" => null,
                _ when action.StartsWith("Key:", StringComparison.OrdinalIgnoreCase) => KeyAction.Create(action[4..]),
                _ => null,
            };
            if (a == null && action != "None") Log.Warn($"Config: unknown action '{action}' for button {button}");
            if (a != null) _map[b] = a;
        }
        Log.Info($"Button mapping: {string.Join(", ", _map.Select(kv => $"{kv.Key}={kv.Value.Name}"))}");
    }

    /// <summary>Called for every report. While calibrating, presses go to the calibration callback instead.</summary>
    public void Process(WiiButtons now, Action<WiiButtons>? calibration)
    {
        var changed = now ^ _prev;
        if (changed == WiiButtons.None) return;
        _prev = now;

        foreach (WiiButtons b in Enum.GetValues<WiiButtons>())
        {
            if (b == WiiButtons.None || (changed & b) == 0) continue;
            bool down = (now & b) != 0;
            bool mapped = _map.ContainsKey(b);
            if (mapped) Log.Info($"Button {b} {(down ? "DOWN" : "UP")}");
            else Log.Debug($"Button {b} {(down ? "DOWN" : "UP")} (unmapped)");

            try
            {
                if (!down && _active.Remove(b)) { _map[b].Up(); continue; }
                if (calibration != null) { if (down) calibration(b); continue; }
                if (down && mapped) { _map[b].Down(); _active.Add(b); }
            }
            catch (Exception e) { Log.Error($"Action for {b} failed: {e}"); }
        }
    }

    /// <summary>Releases everything that is still held (Wii lost, pointer stopped, app exit).</summary>
    public void ReleaseAll()
    {
        foreach (var b in _active.ToList())
        {
            try { _map[b].Up(); } catch (Exception e) { Log.Error($"Release of {b} failed: {e.Message}"); }
            Log.Info($"Button {b} released (forced)");
        }
        _active.Clear();
        _prev = WiiButtons.None;
    }
}

public sealed class LeftClickAction(PointerController pointer, int freezeMs) : IButtonAction
{
    public string Name => "LeftClick";

    public void Down()
    {
        pointer.FreezeFor(freezeMs); // pressing A jerks the remote slightly; keep the click where it was aimed
        Input.LeftButton(true);
    }

    public void Up() => Input.LeftButton(false);
}

public sealed class PushToTalkAction(ISpeechBackend speech) : IButtonAction
{
    public string Name => $"PushToTalk({speech.Name})";
    public void Down() => speech.Begin();
    public void Up() => speech.End();
}

/// <summary>Holds a key (combination) while the button is held, e.g. "Key:plus" (MathQuest: + = „Ja“).</summary>
public sealed class KeyAction(string spec, IReadOnlyList<Input.Key> keys) : IButtonAction
{
    public string Name => $"Key({spec})";
    public void Down() => Input.Keys(keys, true);
    public void Up() => Input.Keys(keys, false);

    /// <summary>Same notation as Handy's shortcuts ("f13", "ctrl+shift+f9"), plus "plus"/"minus" for the numpad keys.</summary>
    public static KeyAction? Create(string spec)
    {
        var keys = HandySpeechBackend.ParseHotkey(spec);
        return keys == null ? null : new KeyAction(spec, keys);
    }
}

/// <summary>Diagnostics: records 30 s of raw pointer data (CSV in the log folder) for tuning.</summary>
public sealed class RecordTraceAction(PointerController pointer) : IButtonAction
{
    public string Name => "RecordPointerTrace";
    public void Down() => pointer.StartTrace(30);
    public void Up() { }
}
