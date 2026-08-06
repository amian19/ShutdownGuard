using ShutdownGuard.Models;
using ShutdownGuard.ViewModels;
using Xunit;

namespace ShutdownGuard.Tests;

public class SettingsViewModelTests
{
    private static readonly DateTimeOffset LocalNow = DateTimeOffset.Now;
    private static readonly TimeSpan LocalOffset = LocalNow.Offset;

    private static DateTimeOffset D(int year, int month, int day, int hour, int minute)
        => new(year, month, day, hour, minute, 0, LocalOffset);

    // ── LoadConfig ───────────────────────────────────────────────

    [Fact]
    public void LoadConfig_EnabledTrue_2345_DryRunFalse_RunAtStartupTrue()
    {
        var config = new AppConfig
        {
            RunAtStartup = true,
            Shutdown = new ShutdownPlan
            {
                Enabled = true,
                ShutdownTime = new TimeOnly(23, 45),
                DryRun = false
            }
        };
        var nextShutdown = D(2026, 8, 6, 23, 45);

        var vm = new SettingsViewModel();
        vm.Load(config, nextShutdown);

        Assert.True(vm.Enabled);
        Assert.Equal(23, vm.Hour);
        Assert.Equal(45, vm.Minute);
        Assert.False(vm.DryRun);
        Assert.True(vm.RunAtStartup);
    }

    [Fact]
    public void LoadConfig_Disabled_ShowsDisabledNextShutdown()
    {
        var config = new AppConfig
        {
            RunAtStartup = false,
            Shutdown = new ShutdownPlan
            {
                Enabled = false,
                ShutdownTime = new TimeOnly(23, 0),
                DryRun = true
            }
        };

        var vm = new SettingsViewModel();
        vm.Load(config, null);

        Assert.False(vm.Enabled);
        Assert.Equal("Disabled", vm.NextShutdownText);
    }

    // ── ToShutdownPlan ───────────────────────────────────────────

    [Fact]
    public void ToShutdownPlan_ConvertsCorrectly()
    {
        var vm = new SettingsViewModel();
        vm.Load(new AppConfig(), null);

        vm.Enabled = true;
        vm.Hour = 21;
        vm.Minute = 30;
        vm.DryRun = false;

        var plan = vm.ToShutdownPlan();

        Assert.True(plan.Enabled);
        Assert.Equal(new TimeOnly(21, 30), plan.ShutdownTime);
        Assert.False(plan.DryRun);
    }

    [Fact]
    public void ToShutdownPlan_DisabledPlan()
    {
        var vm = new SettingsViewModel();
        vm.Load(new AppConfig(), null);

        vm.Enabled = false;
        vm.Hour = 8;
        vm.Minute = 15;

        var plan = vm.ToShutdownPlan();

        Assert.False(plan.Enabled);
        Assert.Equal(new TimeOnly(8, 15), plan.ShutdownTime);
    }

    // ── CancelDoesNotMutateOriginal ──────────────────────────────

    [Fact]
    public void CancelDoesNotMutateOriginal()
    {
        var original = new AppConfig
        {
            RunAtStartup = false,
            Shutdown = new ShutdownPlan
            {
                Enabled = false,
                ShutdownTime = new TimeOnly(23, 0),
                DryRun = true
            }
        };

        var vm = new SettingsViewModel();
        vm.Load(original, null);

        // Modify the working copy
        vm.Enabled = true;
        vm.Hour = 20;
        vm.Minute = 0;
        vm.DryRun = false;
        vm.RunAtStartup = true;

        // Original must be unchanged
        Assert.False(original.Shutdown.Enabled);
        Assert.Equal(new TimeOnly(23, 0), original.Shutdown.ShutdownTime);
        Assert.True(original.Shutdown.DryRun);
        Assert.False(original.RunAtStartup);

        // Working copy reflects edits
        Assert.True(vm.Enabled);
        Assert.Equal(20, vm.Hour);
        Assert.Equal(0, vm.Minute);
        Assert.False(vm.DryRun);
        Assert.True(vm.RunAtStartup);
    }

    // ── DryRunDefault ────────────────────────────────────────────

    [Fact]
    public void DryRunDefault_IsTrue()
    {
        var config = new AppConfig
        {
            Shutdown = new ShutdownPlan
            {
                Enabled = false,
                ShutdownTime = new TimeOnly(23, 0),
                DryRun = true
            }
        };

        var vm = new SettingsViewModel();
        vm.Load(config, null);

        Assert.True(vm.DryRun);
    }

    // ── DefaultValues ────────────────────────────────────────────

    [Fact]
    public void DefaultValues_EnabledFalse_DryRunTrue()
    {
        var config = new AppConfig();

        var vm = new SettingsViewModel();
        vm.Load(config, null);

        Assert.False(vm.Enabled);
        Assert.True(vm.DryRun);
        Assert.False(vm.RunAtStartup);
        Assert.Equal(23, vm.Hour);
        Assert.Equal(0, vm.Minute);
    }

    // ── Hour Bounds ──────────────────────────────────────────────

    [Fact]
    public void Hour_CannotExceed23()
    {
        var vm = new SettingsViewModel();
        vm.Load(new AppConfig(), null);

        vm.Hour = 24;
        Assert.Equal(23, vm.Hour);

        vm.Hour = 99;
        Assert.Equal(23, vm.Hour);
    }

    [Fact]
    public void Hour_CannotBeNegative()
    {
        var vm = new SettingsViewModel();
        vm.Load(new AppConfig(), null);

        vm.Hour = -1;
        Assert.Equal(0, vm.Hour);

        vm.Hour = -99;
        Assert.Equal(0, vm.Hour);
    }

    // ── Minute Bounds ────────────────────────────────────────────

    [Fact]
    public void Minute_CannotExceed59()
    {
        var vm = new SettingsViewModel();
        vm.Load(new AppConfig(), null);

        vm.Minute = 60;
        Assert.Equal(59, vm.Minute);

        vm.Minute = 99;
        Assert.Equal(59, vm.Minute);
    }

    [Fact]
    public void Minute_CannotBeNegative()
    {
        var vm = new SettingsViewModel();
        vm.Load(new AppConfig(), null);

        vm.Minute = -1;
        Assert.Equal(0, vm.Minute);

        vm.Minute = -99;
        Assert.Equal(0, vm.Minute);
    }

    // ── ToAppConfig ──────────────────────────────────────────────

    [Fact]
    public void ToAppConfig_IncludesRunAtStartup()
    {
        var vm = new SettingsViewModel();
        vm.Load(new AppConfig(), null);

        vm.RunAtStartup = true;
        vm.Enabled = true;
        vm.Hour = 8;
        vm.Minute = 30;

        var config = vm.ToAppConfig();

        Assert.True(config.RunAtStartup);
        Assert.True(config.Shutdown.Enabled);
        Assert.Equal(new TimeOnly(8, 30), config.Shutdown.ShutdownTime);
    }

    // ── FormatNextShutdown ───────────────────────────────────────

    [Fact]
    public void FormatNextShutdown_Disabled()
    {
        var result = SettingsViewModel.FormatNextShutdown(null, false);
        Assert.Equal("Disabled", result);
    }

    [Fact]
    public void FormatNextShutdown_NullNext_Enabled()
    {
        var result = SettingsViewModel.FormatNextShutdown(null, true);
        Assert.Equal("Disabled", result);
    }

    [Fact]
    public void FormatNextShutdown_Today()
    {
        var now = DateTimeOffset.Now;
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, 23, 0, 0, now.Offset);

        var result = SettingsViewModel.FormatNextShutdown(today, true);

        Assert.StartsWith("Today,", result);
        Assert.Contains("23:00", result);
    }

    [Fact]
    public void FormatNextShutdown_Tomorrow()
    {
        var now = DateTimeOffset.Now;
        var tomorrow = new DateTimeOffset(now.Year, now.Month, now.Day, 23, 0, 0, now.Offset).AddDays(1);

        var result = SettingsViewModel.FormatNextShutdown(tomorrow, true);

        Assert.StartsWith("Tomorrow,", result);
        Assert.Contains("23:00", result);
    }

    [Fact]
    public void FormatNextShutdown_FutureDate()
    {
        var future = new DateTimeOffset(2026, 12, 25, 23, 0, 0, LocalOffset);

        var result = SettingsViewModel.FormatNextShutdown(future, true);

        Assert.Equal("2026-12-25 23:00", result);
    }
}
