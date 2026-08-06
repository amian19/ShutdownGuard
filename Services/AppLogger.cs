using System.Text;
using System.IO;

namespace ShutdownGuard.Services;

public static class AppLogger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShutdownGuard",
        "logs");

    private static readonly object _lock = new();

    public static void Init()
    {
        Directory.CreateDirectory(LogDir);
    }

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Warn(string message)
    {
        Write("WARN", message);
    }

    public static void Error(string message)
    {
        Write("ERROR", message);
    }

    private static void Write(string level, string message)
    {
        var now = DateTimeOffset.Now;
        var logFile = Path.Combine(LogDir, $"shutdownguard-{now:yyyy-MM-dd}.log");
        var line = $"{now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";

        lock (_lock)
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(logFile, line, Encoding.UTF8);
        }
    }
}
