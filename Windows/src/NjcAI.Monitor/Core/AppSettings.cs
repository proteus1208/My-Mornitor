using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace NjcAI.Monitor.Core;

internal sealed class AppSettings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "NJC AI Monitor";
    private const string StartupShortcutName = "NJC AI Monitor.lnk";

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
    public ColorTheme Theme { get; set; } = ColorTheme.BitcoinOrange;
    public AgentKind? QuotaPrimaryAgent1 { get; set; } = AgentKind.Codex;
    public AgentKind? QuotaPrimaryAgent2 { get; set; } = AgentKind.Grok;
    public bool ShowCodexQuotaProgress { get; set; }
    public bool ShowClaudeQuotaProgress { get; set; } = true;
    public bool ShowGrokQuotaProgress { get; set; }
    public string WeatherLocationName { get; set; } = "Sydney";
    public double WeatherLatitude { get; set; } = -33.8688;
    public double WeatherLongitude { get; set; } = 151.2093;

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NJC AI Monitor");

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (settings is not null)
                {
                    if (!Enum.IsDefined(settings.Theme))
                    {
                        settings.Theme = ColorTheme.BitcoinOrange;
                    }
                    settings.NormalizeQuotaSettings();
                    settings.LaunchAtLogin = IsLaunchAtLoginEnabled();
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
        NormalizeQuotaSettings();
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
        SetLaunchAtLogin(LaunchAtLogin);
    }

    private void NormalizeQuotaSettings()
    {
        if (!IsQuotaAgent(QuotaPrimaryAgent1))
        {
            QuotaPrimaryAgent1 = null;
        }
        if (!IsQuotaAgent(QuotaPrimaryAgent2) || QuotaPrimaryAgent2 == QuotaPrimaryAgent1)
        {
            QuotaPrimaryAgent2 = null;
        }
    }

    internal static bool IsQuotaAgent(AgentKind? kind) => kind is
        AgentKind.Codex or AgentKind.Claude or AgentKind.Grok;

    private static bool IsLaunchAtLoginEnabled()
    {
        return File.Exists(StartupShortcutPath) || HasRegistryStartupValue();
    }

    private static void SetLaunchAtLogin(bool enabled)
    {
        if (enabled)
        {
            // The Startup shortcut launches with an application-level delay. It is
            // manageable by standard Windows accounts and avoids racing USB services
            // or secondary drives immediately after sign-in.
            if (CreateStartupShortcut())
            {
                DeleteRegistryStartupValue();
                return;
            }

            // Keep the previous Run-key behavior as a no-prompt fallback when Windows
            // Script Host shortcut creation is unavailable.
            SetRegistryStartupValue();
            return;
        }

        DeleteStartupShortcut();
        DeleteRegistryStartupValue();
    }

    private static string StartupShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        StartupShortcutName);

    private static bool CreateStartupShortcut()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return false;
        }

        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(StartupShortcutPath)!);
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return false;
            }

            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                [StartupShortcutPath]);
            if (shortcut is null)
            {
                return false;
            }

            var shortcutType = shortcut.GetType();
            SetShortcutProperty(shortcutType, shortcut, "TargetPath", executable);
            SetShortcutProperty(shortcutType, shortcut, "Arguments", "--startup --background");
            SetShortcutProperty(shortcutType, shortcut, "WorkingDirectory", Path.GetDirectoryName(executable)!);
            SetShortcutProperty(shortcutType, shortcut, "IconLocation", $"{executable},0");
            SetShortcutProperty(shortcutType, shortcut, "Description", "Launch NJC AI Monitor after Windows sign-in");
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            return File.Exists(StartupShortcutPath);
        }
        catch
        {
            return false;
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static void SetShortcutProperty(Type shortcutType, object shortcut, string name, string value)
    {
        shortcutType.InvokeMember(name, BindingFlags.SetProperty, null, shortcut, [value]);
    }

    private static void ReleaseComObject(object? value)
    {
        try
        {
            if (value is not null && Marshal.IsComObject(value))
            {
                _ = Marshal.FinalReleaseComObject(value);
            }
        }
        catch
        {
            // COM cleanup should never affect launch-at-login registration.
        }
    }

    private static void DeleteStartupShortcut()
    {
        try
        {
            File.Delete(StartupShortcutPath);
        }
        catch
        {
            // The UI reports the effective value on next load; running remains unaffected.
        }
    }

    private static bool HasRegistryStartupValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return HasRunValue(key, RunValue);
        }
        catch
        {
            return false;
        }
    }

    private static void SetRegistryStartupValue()
    {
        try
        {
            var executable = Environment.ProcessPath ?? string.Empty;
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(RunValue, $"\"{executable}\" --background");
        }
        catch
        {
            // The UI reports the effective value on next load; running remains unaffected.
        }
    }

    private static void DeleteRegistryStartupValue()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.DeleteValue(RunValue, false);
        }
        catch
        {
            // The UI reports the effective value on next load; running remains unaffected.
        }
    }

    private static bool HasRunValue(RegistryKey? key, string name) =>
        key?.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value);

}
