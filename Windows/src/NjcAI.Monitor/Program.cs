using System.Windows;
using NjcAI.Monitor.Core;
using NjcAI.Monitor.Rendering;
using NjcAI.Monitor.Services;
using Application = System.Windows.Application;

namespace NjcAI.Monitor;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception)
        {
            Log.Error("Fatal startup error", exception);
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "NJC-AI-Monitor.crash.log"), exception.ToString());
            }
            catch
            {
                // The executable directory can be read-only after installation.
            }
            return 2;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            return SelfTest.Run();
        }

        var snapshotIndex = Array.FindIndex(args,
            argument => string.Equals(argument, "--snapshot", StringComparison.OrdinalIgnoreCase));
        if (snapshotIndex >= 0)
        {
            var output = snapshotIndex + 1 < args.Length
                ? args[snapshotIndex + 1]
                : Path.Combine(Environment.CurrentDirectory, "NJC-AI-Monitor-snapshot.png");
            var idleDemo = args.Contains("--demo-idle", StringComparer.OrdinalIgnoreCase);
            var completedDemo = args.Contains("--demo-completed", StringComparer.OrdinalIgnoreCase);
            var demo = idleDemo || completedDemo || args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
            return WriteSnapshot(output, demo, idleDemo, completedDemo);
        }

        using var singleInstance = SingleInstanceGuard.TryAcquire();
        if (!singleInstance.HasOwnership)
        {
            Log.Warn("Secondary launch blocked by the single-instance guard");
            System.Windows.MessageBox.Show(
                "NJC AI Monitor 已经在运行。\n\n请使用系统托盘中的现有图标，不要再次启动 EXE。",
                "NJC AI Monitor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return 0;
        }

        // Migrate settings and caches before collector field initializers read the new data directory.
        _ = AppSettings.Load();
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var controller = new AppController(application, args);
        controller.Start();
        return application.Run();
    }

    private static int WriteSnapshot(string path, bool demo, bool idleDemo, bool completedDemo)
    {
        try
        {
            DashboardSnapshot snapshot;
            if (demo)
            {
                snapshot = DemoData.Create(idleDemo, completedDemo);
            }
            else
            {
                using var system = new SystemMetricsCollector();
                using var markets = new MarketDataCollector();
                using var news = new NewsDataCollector();
                using var weather = new WeatherDataCollector();
                _ = system.Collect();
                Thread.Sleep(1100);
                var snapshotSettings = AppSettings.Load();
                snapshot = new DashboardSnapshot(
                    system.Collect(),
                    new AgentUsageCollector().Collect(),
                    markets.CollectAsync(CancellationToken.None).GetAwaiter().GetResult(),
                    news.CollectAsync(CancellationToken.None).GetAwaiter().GetResult(),
                    weather.CollectAsync(
                        snapshotSettings.WeatherLocationName,
                        snapshotSettings.WeatherLatitude,
                        snapshotSettings.WeatherLongitude,
                        CancellationToken.None).GetAwaiter().GetResult());
            }
            var settings = AppSettings.Load();
            var renderer = new DashboardRenderer();
            using var bitmap = renderer.Render(snapshot, settings, "Windows 快照模式");
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Log.Info($"Snapshot written: {Path.GetFullPath(path)}");
            return 0;
        }
        catch (Exception exception)
        {
            Log.Error("Snapshot failed", exception);
            return 1;
        }
    }
}
