using ShutdownGuard.Models;
using ShutdownGuard.Services;
using Xunit;

namespace ShutdownGuard.Tests;

public class FileDailyCancellationStoreTests : IDisposable
{
    private readonly string _dir;

    public FileDailyCancellationStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"ShutdownGuard_State_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void MissingState_IsNotCancelled()
    {
        var store = new FileDailyCancellationStore(_dir);
        Assert.Null(store.LoadCancelledDate());
        Assert.False(File.Exists(store.StatePath));
    }

    [Fact]
    public void SaveCancelledDate_WritesStateJson_NotConfigJson()
    {
        var store = new FileDailyCancellationStore(_dir);
        store.SaveCancelledDate(new DateOnly(2026, 8, 6));

        Assert.True(File.Exists(store.StatePath));
        var json = File.ReadAllText(store.StatePath);
        Assert.Contains("cancelledShutdownDate", json);
        Assert.Contains("2026-08-06", json);
        Assert.False(File.Exists(Path.Combine(_dir, "config.json")));
    }

    [Fact]
    public void TodayCancelled_LoadsToday()
    {
        var store = new FileDailyCancellationStore(_dir);
        store.SaveCancelledDate(new DateOnly(2026, 8, 6));
        Assert.Equal(new DateOnly(2026, 8, 6), store.LoadCancelledDate());
    }

    [Fact]
    public void YesterdayCancelled_StillLoadsDate_ButSessionTreatsAsExpired()
    {
        // Store returns the date; SessionController compares to today.
        var store = new FileDailyCancellationStore(_dir);
        store.SaveCancelledDate(new DateOnly(2026, 8, 5));
        Assert.Equal(new DateOnly(2026, 8, 5), store.LoadCancelledDate());
    }

    [Fact]
    public void CorruptState_ReturnsNull_NotCancelled()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "state.json"), "{abc");

        var store = new FileDailyCancellationStore(_dir);
        Assert.Null(store.LoadCancelledDate());
    }

    [Fact]
    public void CancelThenConfigSave_CancellationRemains()
    {
        var stateStore = new FileDailyCancellationStore(_dir);
        var configStore = new ConfigStore(_dir);

        stateStore.SaveCancelledDate(new DateOnly(2026, 8, 6));

        configStore.Save(new AppConfig
        {
            RunAtStartup = true,
            Shutdown = new ShutdownPlan
            {
                Enabled = true,
                ReminderStartTime = new TimeOnly(19, 30),
                DryRun = true
            }
        });

        // Config save must not wipe or own cancellation.
        var configJson = File.ReadAllText(Path.Combine(_dir, "config.json"));
        Assert.DoesNotContain("cancelledShutdownDate", configJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new DateOnly(2026, 8, 6), stateStore.LoadCancelledDate());

        var loaded = configStore.Load();
        Assert.Equal(new TimeOnly(19, 30), loaded.Shutdown.ReminderStartTime);
        Assert.True(loaded.RunAtStartup);
    }

    [Fact]
    public void DefaultProductionPath_IsUnderLocalAppData()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShutdownGuard",
            "state.json");

        var store = new FileDailyCancellationStore();
        Assert.Equal(expected, store.StatePath);
    }
}
