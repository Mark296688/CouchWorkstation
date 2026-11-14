using System.Collections.Concurrent;

namespace CouchWorkstation;

/// <summary>
/// Small asynchronous file logger. Writes never block the caller (the Wii reader thread logs too).
/// One file per day in %LOCALAPPDATA%\CouchWorkstation\logs, files older than 7 days are deleted,
/// and a single day's file is capped so debug logging cannot fill the disk.
/// </summary>
public static class Log
{
    private const long MaxFileBytes = 50L * 1024 * 1024;

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CouchWorkstation", "logs");

    public static bool DebugEnabled { get; set; }

    private static readonly BlockingCollection<string> Queue = new(20000);
    private static Thread? _writer;

    public static void Init(bool debug)
    {
        DebugEnabled = debug;
        System.IO.Directory.CreateDirectory(Directory);
        foreach (var f in new DirectoryInfo(Directory).GetFiles("couch-*.log"))
            if (f.LastWriteTime < DateTime.Now.AddDays(-7)) f.Delete();
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "LogWriter" };
        _writer.Start();
    }

    public static void Debug(string msg) { if (DebugEnabled) Write("DBG", msg); }
    public static void Info(string msg) => Write("INF", msg);
    public static void Warn(string msg) => Write("WRN", msg);
    public static void Error(string msg) => Write("ERR", msg);

    private static void Write(string level, string msg) =>
        Queue.TryAdd($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}");

    private static void WriteLoop()
    {
        StreamWriter? w = null;
        string? currentPath = null;
        long written = 0;
        foreach (var line in Queue.GetConsumingEnumerable())
        {
            try
            {
                var path = Path.Combine(Directory, $"couch-{DateTime.Now:yyyyMMdd}.log");
                if (path != currentPath)
                {
                    w?.Dispose();
                    w = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                    currentPath = path;
                    written = new FileInfo(path).Length;
                }
                if (written > MaxFileBytes && !line.Contains("[ERR]")) continue;
                w!.WriteLine(line);
                written += line.Length + 2;
            }
            catch { /* logging must never take the app down */ }
        }
        w?.Dispose();
    }

    public static void Shutdown()
    {
        Queue.CompleteAdding();
        _writer?.Join(1000);
    }
}
