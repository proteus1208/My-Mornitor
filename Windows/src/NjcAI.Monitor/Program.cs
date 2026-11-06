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
            return WriteSnapshot(output, demo, idleDemo, completedDemo, ParseThemeOverride(args));
        }

        var startupLaunch = args.Contains("--startup", StringComparer.OrdinalIgnoreCase);
        var backgroundLaunch = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        if (startupLaunch)
        {
            Thread.Sleep(TimeSpan.FromSeconds(20));
        }

        using var singleInstance = SingleInstanceGuard.TryAcquire();
        if (!singleInstance.HasOwnership)
        {
            Log.Warn("Secondary launch blocked by the single-instance guard");
            if (!startupLaunch && !backgroundLaunch)
            {
                System.Windows.MessageBox.Show(
                    "NJC AI Monitor is already running.\n\nUse the existing system tray icon instead of launching the EXE again.",
                    "NJC AI Monitor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            return 0;
        }

        // Migrate settings and caches before collector field initializers read the new data directory.
        _ = AppSettings.Load();
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var controller = new AppController(application, args);
        controller.Start();
        return application.Run();
    }

    private static ColorTheme? ParseThemeOverride(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (!string.Equals(args[index], "--theme", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Enum.TryParse<ColorTheme>(args[index + 1], true, out var theme) &&
                   Enum.IsDefined(theme)
                ? theme
                : null;
        }
        return null;
    }

    private static int WriteSnapshot(
        string path,
        bool demo,
        bool idleDemo,
        bool completedDemo,
        ColorTheme? themeOverride)
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
            if (themeOverride is { } theme)
            {
                settings.Theme = theme;
            }
            if (completedDemo)
            {
                AlignCompletionSnapshotToBrightPhase();
            }
            var renderer = new DashboardRenderer();
            using var bitmap = renderer.Render(snapshot, settings, "Windows snapshot mode");
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

    private static void AlignCompletionSnapshotToBrightPhase()
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            var now = DateTime.Now;
            var phaseMilliseconds = now.TimeOfDay.TotalMilliseconds % 900;
            if (DashboardRenderer.IsCompletionFlashOn(now) &&
                phaseMilliseconds is >= 80 and <= 180)
            {
                return;
            }
            Thread.Sleep(10);
        }
    }
}
