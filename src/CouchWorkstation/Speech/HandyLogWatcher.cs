using System.Diagnostics;
using System.Text;

namespace CouchWorkstation.Speech;

/// <summary>
/// Follows Handy's own log file (it logs at debug level) to learn what Handy is doing, without any
/// API: when the recording is really ready, when the text was pasted, or that nothing was recorded.
/// Lines are timestamped with our own clock when they appear (polling every 25 ms).
/// </summary>
public sealed class HandyLogWatcher : IDisposable
{
    public enum Kind { HotkeyPressed, RecordingReady, HotkeyReleased, Pasted, NoAudio, Error }

    /// <summary>Raised on the watcher thread with the event kind, our timestamp (Stopwatch ticks) and the log line.</summary>
    public event Action<Kind, long, string>? Event;

    private readonly string _path =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "com.pais.handy", "logs", "handy.log");
    private readonly Thread _thread;
    private volatile bool _stop;

    public HandyLogWatcher()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "HandyLogWatcher" };
    }

    public void Start() => _thread.Start();

    private void Run()
    {
        long pos = File.Exists(_path) ? new FileInfo(_path).Length : 0; // only new lines
        var pending = new StringBuilder();
        var buf = new byte[64 * 1024];
        while (!_stop)
        {
            try
            {
                if (File.Exists(_path))
                {
                    using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (fs.Length < pos) pos = 0; // rotated / truncated
                    if (fs.Length > pos)
                    {
                        fs.Seek(pos, SeekOrigin.Begin);
                        int n;
                        while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                        {
                            pos += n;
                            pending.Append(Encoding.UTF8.GetString(buf, 0, n));
                        }
                        long now = Stopwatch.GetTimestamp();
                        var text = pending.ToString();
                        int lastNl = text.LastIndexOf('\n');
                        if (lastNl >= 0)
                        {
                            foreach (var line in text[..lastNl].Split('\n')) Classify(line.TrimEnd('\r'), now);
                            pending.Clear().Append(text[(lastNl + 1)..]);
                        }
                    }
                }
            }
            catch (Exception e) { Log.Debug($"Handy log watcher: {e.Message}"); }
            Thread.Sleep(25);
        }
    }

    private void Classify(string line, long ts)
    {
        Kind? k =
            line.Contains("binding=transcribe,") && line.Contains("state=Pressed") ? Kind.HotkeyPressed :
            line.Contains("binding=transcribe,") && line.Contains("state=Released") ? Kind.HotkeyReleased :
            line.Contains("recording is ready") ? Kind.RecordingReady :
            line.Contains("Text pasted successfully") ? Kind.Pasted :
            line.Contains("produced no audio samples") || line.Contains("empty transcription", StringComparison.OrdinalIgnoreCase) ? Kind.NoAudio :
            line.Contains("][ERROR]") ? Kind.Error :
            null;
        if (k != null) Event?.Invoke(k.Value, ts, line);
    }

    public void Dispose()
    {
        _stop = true;
        if (_thread.IsAlive) _thread.Join(500);
    }
}
