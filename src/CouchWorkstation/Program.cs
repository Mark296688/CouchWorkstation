using CouchWorkstation.UI;

namespace CouchWorkstation;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Only one instance may own the Wii Remote and inject input.
        using var mutex = new Mutex(true, "CouchWorkstation.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("Couch Workstation läuft bereits.", "Couch Workstation");
            return;
        }

        ApplicationConfiguration.Initialize(); // per-monitor DPI awareness first: all coordinates are physical pixels
        var config = AppConfig.Load();
        Log.Init(config.DebugLogging);
        Log.Info($"=== Couch Workstation start (config {AppConfig.FilePath}) ===");

        Application.ThreadException += (_, e) => Log.Error($"UI exception: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error($"Unhandled exception: {e.ExceptionObject}");

        using (var app = new App(config))
        {
            Application.Run(new MainForm(app)); // the form starts the app once it is shown
            Log.Info("Shutting down");
        }
        Log.Info("=== Couch Workstation stopped ===");
        Log.Shutdown();
    }
}
