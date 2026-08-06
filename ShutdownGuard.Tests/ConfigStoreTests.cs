using System.Text.Json;
using ShutdownGuard.Core;
using ShutdownGuard.Models;
using ShutdownGuard.Services;
using Xunit;

namespace ShutdownGuard.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _testDir;

    public ConfigStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ShutdownGuard_Test_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public void Load_MissingConfig_ReturnsEnabledDefaults()
    {
        var store = new ConfigStore(_testDir);
        var config = store.Load();

        Assert.False(config.RunAtStartup);
        Assert.True(config.Shutdown.Enabled);
        Assert.True(config.Shutdown.DryRun);
        Assert.Equal(new TimeOnly(18, 0), config.Shutdown.ReminderStartTime);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsSafeDisabledFallback()
    {
        Directory.CreateDirectory(_testDir);
        File.WriteAllText(Path.Combine(_testDir, "config.json"), "{abc");

        var store = new ConfigStore(_testDir);
        var config = store.Load();

        Assert.False(config.RunAtStartup);
        Assert.False(config.Shutdown.Enabled);
        Assert.True(config.Shutdown.DryRun);
        Assert.Equal(new TimeOnly(18, 0), config.Shutdown.ReminderStartTime);
    }

    [Fact]
    public void Load_OldShutdownTime_LessThan22_MigratesToReminder()
    {
        Directory.CreateDirectory(_testDir);
        var json = """
            {
              "runAtStartup": false,
              "shutdown": {
                "enabled": true,
                "shutdownTime": "20:30:00",
                "dryRun": true
              }
            }
            """;
        File.WriteAllText(Path.Combine(_testDir, "config.json"), json);

        var store = new ConfigStore(_testDir);
        var config = store.Load();

        Assert.True(config.Shutdown.Enabled);
        Assert.Equal(new TimeOnly(20, 30), config.Shutdown.ReminderStartTime);
        Assert.True(config.Shutdown.DryRun);
    }

    [Fact]
    public void Load_OldShutdownTime_GreaterOrEqual22_FallsBackTo1800()
    {
        Directory.CreateDirectory(_testDir);
        var json = """
            {
              "runAtStartup": true,
              "shutdown": {
                "enabled": true,
                "shutdownTime": "23:00:00",
                "dryRun": false
              }
            }
            """;
        File.WriteAllText(Path.Combine(_testDir, "config.json"), json);

        var store = new ConfigStore(_testDir);
        var config = store.Load();

        Assert.True(config.RunAtStartup);
        Assert.True(config.Shutdown.Enabled);
        Assert.False(config.Shutdown.DryRun);
        Assert.Equal(new TimeOnly(18, 0), config.Shutdown.ReminderStartTime);
    }

    [Fact]
    public void SaveAndLoad_NewSchema_Roundtrip()
    {
        var store = new ConfigStore(_testDir);

        var original = new AppConfig
        {
            RunAtStartup = true,
            Shutdown = new ShutdownPlan
            {
                Enabled = true,
                ReminderStartTime = new TimeOnly(19, 15),
                DryRun = false
            }
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original.RunAtStartup, loaded.RunAtStartup);
        Assert.Equal(original.Shutdown.Enabled, loaded.Shutdown.Enabled);
        Assert.Equal(original.Shutdown.ReminderStartTime, loaded.Shutdown.ReminderStartTime);
        Assert.Equal(original.Shutdown.DryRun, loaded.Shutdown.DryRun);

        var savedJson = File.ReadAllText(Path.Combine(_testDir, "config.json"));
        Assert.Contains("reminderStartTime", savedJson);
        Assert.DoesNotContain("shutdownTime", savedJson);
        Assert.DoesNotContain("22:00", savedJson);
    }

    [Fact]
    public void Load_OldM1Config_UsesFreshPlanDefaults()
    {
        Directory.CreateDirectory(_testDir);
        File.WriteAllText(Path.Combine(_testDir, "config.json"), """{"runAtStartup": true}""");

        var store = new ConfigStore(_testDir);
        var config = store.Load();

        Assert.True(config.RunAtStartup);
        Assert.True(config.Shutdown.Enabled);
        Assert.Equal(new TimeOnly(18, 0), config.Shutdown.ReminderStartTime);
        Assert.True(config.Shutdown.DryRun);
    }

    [Fact]
    public void Load_PrefersReminderStartTime_OverLegacyShutdownTime()
    {
        Directory.CreateDirectory(_testDir);
        var json = """
            {
              "shutdown": {
                "enabled": true,
                "reminderStartTime": "19:00:00",
                "shutdownTime": "20:30:00",
                "dryRun": true
              }
            }
            """;
        File.WriteAllText(Path.Combine(_testDir, "config.json"), json);

        var config = new ConfigStore(_testDir).Load();
        Assert.Equal(new TimeOnly(19, 0), config.Shutdown.ReminderStartTime);
    }

    [Fact]
    public void FreshDefault_And_CorruptFallback_DifferOnEnabled()
    {
        var fresh = ConfigStore.CreateFreshDefault();
        var corrupt = ConfigStore.CreateCorruptFallback();

        Assert.True(fresh.Shutdown.Enabled);
        Assert.False(corrupt.Shutdown.Enabled);
        Assert.Equal(ShutdownPolicy.DefaultReminderStartTime, fresh.Shutdown.ReminderStartTime);
        Assert.Equal(ShutdownPolicy.DefaultReminderStartTime, corrupt.Shutdown.ReminderStartTime);
    }

    [Fact]
    public void ReminderStartTime_JsonRoundtrip_ProducesExpectedFormat()
    {
        var plan = new ShutdownPlan
        {
            Enabled = true,
            ReminderStartTime = new TimeOnly(18, 0),
            DryRun = true
        };

        var json = JsonSerializer.Serialize(plan, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.Contains(@"""reminderStartTime"":""18:00:00""", json);
    }

    [Fact]
    public void ReminderStartTime_JsonRoundtrip_FullCycle()
    {
        var plan = new ShutdownPlan
        {
            Enabled = true,
            ReminderStartTime = new TimeOnly(8, 15, 30),
            DryRun = false
        };

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(plan, options);
        var deserialized = JsonSerializer.Deserialize<ShutdownPlan>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(plan.ReminderStartTime, deserialized!.ReminderStartTime);
        Assert.Equal(plan.Enabled, deserialized.Enabled);
        Assert.Equal(plan.DryRun, deserialized.DryRun);
    }

    [Fact]
    public void Save_DoesNotWriteCancelledShutdownDate_IntoConfigJson()
    {
        var store = new ConfigStore(_testDir);
        store.Save(new AppConfig
        {
            RunAtStartup = false,
            Shutdown = new ShutdownPlan
            {
                Enabled = true,
                ReminderStartTime = new TimeOnly(18, 0),
                DryRun = true
            }
        });

        var json = File.ReadAllText(Path.Combine(_testDir, "config.json"));
        Assert.DoesNotContain("cancelledShutdownDate", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CancelledShutdownDate", json);
    }

    [Fact]
    public void Load_IgnoresLegacyCancelledShutdownDate_InConfigJson()
    {
        Directory.CreateDirectory(_testDir);
        File.WriteAllText(
            Path.Combine(_testDir, "config.json"),
            """
            {
              "runAtStartup": false,
              "cancelledShutdownDate": "2026-08-06",
              "shutdown": {
                "enabled": true,
                "reminderStartTime": "18:00:00",
                "dryRun": true
              }
            }
            """);

        var config = new ConfigStore(_testDir).Load();

        // AppConfig has no CancelledShutdownDate — ensure load still succeeds.
        Assert.True(config.Shutdown.Enabled);
        Assert.Equal(new TimeOnly(18, 0), config.Shutdown.ReminderStartTime);
    }
}
