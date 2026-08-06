using System.Text.Json;
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
    public void Load_MissingConfig_ReturnsDefaults()
    {
        var store = new ConfigStore(_testDir);
        var config = store.Load();

        Assert.False(config.RunAtStartup);
        Assert.NotNull(config.Shutdown);
        Assert.False(config.Shutdown.Enabled);
        Assert.True(config.Shutdown.DryRun);
        Assert.Equal(new TimeOnly(23, 0), config.Shutdown.ShutdownTime);
    }

    [Fact]
    public void Load_OldM1Config_MigratesCorrectly()
    {
        // M1 config only had RunAtStartup
        Directory.CreateDirectory(_testDir);
        var m1Json = @"{""runAtStartup"": true}";
        File.WriteAllText(Path.Combine(_testDir, "config.json"), m1Json);

        var store = new ConfigStore(_testDir);
        var config = store.Load();

        Assert.True(config.RunAtStartup);
        Assert.NotNull(config.Shutdown); // Must not be null
        Assert.False(config.Shutdown.Enabled);
        Assert.True(config.Shutdown.DryRun);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsSafeDefaults()
    {
        Directory.CreateDirectory(_testDir);
        File.WriteAllText(Path.Combine(_testDir, "config.json"), "{abc");

        var store = new ConfigStore(_testDir);
        var config = store.Load();

        // Must not crash — return safe defaults
        Assert.False(config.RunAtStartup);
        Assert.False(config.Shutdown.Enabled);
        Assert.True(config.Shutdown.DryRun);
    }

    [Fact]
    public void SaveAndLoad_Roundtrip_Equivalent()
    {
        var store = new ConfigStore(_testDir);

        var original = new AppConfig
        {
            RunAtStartup = true,
            Shutdown = new ShutdownPlan
            {
                Enabled = true,
                ShutdownTime = new TimeOnly(22, 30),
                DryRun = false
            }
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original.RunAtStartup, loaded.RunAtStartup);
        Assert.Equal(original.Shutdown.Enabled, loaded.Shutdown.Enabled);
        Assert.Equal(original.Shutdown.ShutdownTime, loaded.Shutdown.ShutdownTime);
        Assert.Equal(original.Shutdown.DryRun, loaded.Shutdown.DryRun);
    }

    [Fact]
    public void SaveAndLoad_Roundtrip_Defaults()
    {
        var store = new ConfigStore(_testDir);

        var original = new AppConfig(); // All defaults
        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original.RunAtStartup, loaded.RunAtStartup);
        Assert.Equal(original.Shutdown.Enabled, loaded.Shutdown.Enabled);
        Assert.Equal(original.Shutdown.DryRun, loaded.Shutdown.DryRun);
    }

    [Fact]
    public void Load_ShutdownSectionMissing_DefaultsShutdown()
    {
        // JSON with runAtStartup but no shutdown section
        Directory.CreateDirectory(_testDir);
        var json = @"{""runAtStartup"": false}";
        File.WriteAllText(Path.Combine(_testDir, "config.json"), json);

        var store = new ConfigStore(_testDir);
        var config = store.Load();

        Assert.NotNull(config.Shutdown);
        Assert.False(config.Shutdown.Enabled);
        Assert.True(config.Shutdown.DryRun);
    }

    [Fact]
    public void TimeOnly_JsonRoundtrip_ProducesExpectedFormat()
    {
        var plan = new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        };

        var json = JsonSerializer.Serialize(plan, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.Contains(@"""shutdownTime"":""23:00:00""", json);
    }

    [Fact]
    public void TimeOnly_JsonRoundtrip_FullCycle()
    {
        var plan = new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(8, 15, 30),
            DryRun = false
        };

        var json = JsonSerializer.Serialize(plan, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var deserialized = JsonSerializer.Deserialize<ShutdownPlan>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.NotNull(deserialized);
        Assert.Equal(plan.Enabled, deserialized!.Enabled);
        Assert.Equal(plan.ShutdownTime, deserialized.ShutdownTime);
        Assert.Equal(plan.DryRun, deserialized.DryRun);
    }

    [Fact]
    public void TimeOnly_JsonFormat_UsesColonSeparators()
    {
        var plan = new ShutdownPlan
        {
            ShutdownTime = new TimeOnly(9, 5, 0)
        };

        var json = JsonSerializer.Serialize(plan, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.Contains(@"""shutdownTime"":""09:05:00""", json);
    }
}
