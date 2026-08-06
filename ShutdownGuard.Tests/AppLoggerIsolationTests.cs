using ShutdownGuard.Services;
using Xunit;

namespace ShutdownGuard.Tests;

public class AppLoggerIsolationTests
{
    [Fact]
    public void CurrentLogDirectory_IsNotProductionLocalAppData()
    {
        var production = AppLogger.DefaultLogDirectory;
        var current = AppLogger.CurrentLogDirectory;

        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ShutdownGuard",
                "logs"),
            production);

        Assert.NotEqual(production, current);
        Assert.StartsWith(
            Path.Combine(Path.GetTempPath(), "ShutdownGuard.Tests"),
            current,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Info_WritesToConfiguredTestDirectory_NotProduction()
    {
        var marker = $"isolation-marker-{Guid.NewGuid():N}";
        AppLogger.Info(marker);

        var logFile = Path.Combine(
            AppLogger.CurrentLogDirectory,
            $"shutdownguard-{DateTimeOffset.Now:yyyy-MM-dd}.log");

        Assert.True(File.Exists(logFile));
        var text = File.ReadAllText(logFile);
        Assert.Contains(marker, text);

        // Production path must not contain this marker (best-effort; file may not exist).
        var productionFile = Path.Combine(
            AppLogger.DefaultLogDirectory,
            $"shutdownguard-{DateTimeOffset.Now:yyyy-MM-dd}.log");
        if (File.Exists(productionFile))
        {
            var productionText = File.ReadAllText(productionFile);
            Assert.DoesNotContain(marker, productionText);
        }
    }

    [Fact]
    public void Info_NeverThrows_EvenRepeatedly()
    {
        // Regression: logger I/O must not crash callers (M3.1.2 / M3.5.1).
        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < 5; i++)
                AppLogger.Info($"safe-write-{i}");
        });
        Assert.Null(ex);
    }
}
