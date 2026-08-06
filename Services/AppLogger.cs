using System.Text;
using System.IO;

namespace ShutdownGuard.Services;

public static class AppLogger
{
    private static readonly string DefaultLogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShutdownGuard",
        "logs");

    private static string _logDir = DefaultLogDir;
    private static readonly object _lock = new();

    /// <summary>
    /// Current log directory. Production default is under LocalAppData;
    /// tests may override via <see cref="ConfigureLogDirectory"/>.
    /// </summary>
    internal static string CurrentLogDirectory
    {
        get { lock (_lock) return _logDir; }
    }

    /// <summary>
    /// Production default log directory (never mutated by ConfigureLogDirectory).
    /// </summary>
    internal static string DefaultLogDirectory => DefaultLogDir;

    public static void Init()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(_logDir);
        }
    }

    /// <summary>
    /// Overrides the log root for tests. Pass null to restore the production default.
    /// Must be called before any concurrent logging from other threads expects a stable path.
    /// </summary>
    internal static void ConfigureLogDirectory(string? directory)
    {
        lock (_lock)
        {
            _logDir = string.IsNullOrWhiteSpace(directory) ? DefaultLogDir : directory;
            try
            {
                Directory.CreateDirectory(_logDir);
            }
            catch
            {
                // Never throw from logger configuration.
            }
        }
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
        try
        {
            string logDir;
            lock (_lock)
            {
                logDir = _logDir;
            }

            var now = DateTimeOffset.Now;
            var logFile = Path.Combine(logDir, $"shutdownguard-{now:yyyy-MM-dd}.log");
            var line = $"{now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";

            lock (_lock)
            {
                Directory.CreateDirectory(logDir);
                File.AppendAllText(logFile, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logger must never crash the application.
            // Silently ignore write failures (permission denied, disk full, etc.).
        }
    }
}
