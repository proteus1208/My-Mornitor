namespace NjcAI.Monitor.Core;

internal static class Log
{
    private static readonly object Gate = new();
    private static readonly string PathName = Path.Combine(AppSettings.DataDirectory, "NJC-AI-Monitor.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}: {exception}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppSettings.DataDirectory);
                RotateIfNeeded();
                File.AppendAllText(PathName,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging is intentionally non-fatal.
        }
    }

    private static void RotateIfNeeded()
    {
        var file = new FileInfo(PathName);
        if (!file.Exists || file.Length < 2 * 1024 * 1024)
        {
            return;
        }

        var previous = System.IO.Path.ChangeExtension(PathName, ".previous.log");
        File.Move(PathName, previous, true);
    }
}
