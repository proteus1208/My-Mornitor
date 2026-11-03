using System.Text.Json;
using Microsoft.Win32;

namespace NjcAI.Monitor.Core;

internal sealed class AppSettings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "NJC AI Monitor";
    private static readonly string[] LegacyRunValues = ["Thermalright AI Monitor", "MacTR AI Windows"];

    public AgentKind LeftAgent { get; set; } = AgentKind.Cursor;
    public AgentKind RightAgent { get; set; } = AgentKind.Codex;
    public bool ShowPreviewWhenDisconnected { get; set; } = true;
    public bool LaunchAtLogin { get; set; }
    public bool ShowAgentCompletionNotification { get; set; } = true;
    public bool PlayAgentCompletionSound { get; set; }
    public int JpegQuality { get; set; } = 82;
    public int IdleFramesPerSecond { get; set; } = 2;
    public int ActiveFramesPerSecond { get; set; } = 15;
    public bool RotateDisplay180 { get; set; }
    public string WeatherLocationName { get; set; } = "Sydney";
    public double WeatherLatitude { get; set; } = -33.8688;
    public double WeatherLongitude { get; set; } = 151.2093;

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NJC AI Monitor");

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    private static IReadOnlyList<string> LegacyDataDirectories
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return
            [
                Path.Combine(local, "Thermalright AI Monitor"),
                Path.Combine(local, "MacTR AI"),
            ];
        }
    }

    public static AppSettings Load()
    {
        try
        {
            var source = File.Exists(SettingsPath)
                ? SettingsPath
                : LegacyDataDirectories
                    .Select(directory => Path.Combine(directory, "settings.json"))
                    .FirstOrDefault(File.Exists);
            if (source is not null)
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(source));
                if (settings is not null)
                {
                    settings.LaunchAtLogin = IsLaunchAtLoginEnabled();
                    if (!string.Equals(source, SettingsPath, StringComparison.OrdinalIgnoreCase))
                    {
                        MigrateLegacyData(Path.GetDirectoryName(source)!);
                    }
                    if (settings.LaunchAtLogin)
                    {
                        SetLaunchAtLogin(true);
                    }
                    return settings;
                }
            }
        }
        catch
        {
            // A corrupt settings file should never prevent the monitor from starting.
        }

        return new AppSettings { LaunchAtLogin = IsLaunchAtLoginEnabled() };
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
        SetLaunchAtLogin(LaunchAtLogin);
    }

    private static bool IsLaunchAtLoginEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return HasRunValue(key, RunValue) || LegacyRunValues.Any(name => HasRunValue(key, name));
        }
        catch
        {
            return false;
        }
    }

    private static void SetLaunchAtLogin(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var executable = Environment.ProcessPath ?? string.Empty;
                key.SetValue(RunValue, $"\"{executable}\" --background");
            }
            else
            {
                key.DeleteValue(RunValue, false);
            }
            foreach (var legacyName in LegacyRunValues)
            {
                key.DeleteValue(legacyName, false);
            }
        }
        catch
        {
            // The UI reports the effective value on next load; running remains unaffected.
        }
    }

    private static bool HasRunValue(RegistryKey? key, string name) =>
        key?.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value);

    private static void MigrateLegacyData(string legacyDirectory)
    {
        Directory.CreateDirectory(DataDirectory);
        foreach (var name in new[]
                 {
                     "settings.json", "news-cache.json", "weather-cache.json", "completion-alerts.json",
                 })
        {
            var source = Path.Combine(legacyDirectory, name);
            var destination = Path.Combine(DataDirectory, name);
            if (File.Exists(source) && !File.Exists(destination))
            {
                File.Copy(source, destination);
            }
        }
    }
}
