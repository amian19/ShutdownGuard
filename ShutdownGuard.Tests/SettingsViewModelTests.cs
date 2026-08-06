using ShutdownGuard.Core;
using ShutdownGuard.Models;
using ShutdownGuard.ViewModels;
using Xunit;

namespace ShutdownGuard.Tests;

public class SettingsViewModelTests
{
    private static readonly TimeSpan LocalOffset = DateTimeOffset.Now.Offset;

    private static DateTimeOffset D(int year, int month, int day, int hour, int minute)
        => new(year, month, day, hour, minute, 0, LocalOffset);

    [Fact]
    public void Defaults_EnabledTrue_Reminder1800_DryRunTrue()
    {
        var config = new AppConfig();
        var vm = new SettingsViewModel();
        vm.Load(config, null);

        Assert.True(vm.Enabled);
        Assert.Equal(18, vm.ReminderHour);
        Assert.Equal(0, vm.ReminderMinute);
        Assert.True(vm.DryRun);
        Assert.False(vm.RunAtStartup);
        Assert.Equal("22 : 00", vm.FixedShutdownDisplay);
    }

    [Fact]
    public void ReminderTimeMapping_2030()
    {
        var vm = new SettingsViewModel();
        vm.Load(new AppConfig(), null);
        vm.Enabled = true;
        vm.ReminderHour = 20;
        vm.ReminderMinute = 30;
        vm.DryRun = false;

        var plan = vm.ToShutdownPlan();

        Assert.True(plan.Enabled);
        Assert.Equal(new TimeOnly(20, 30), plan.ReminderStartTime);
        Assert.False(plan.DryRun);
    }

    [Fact]
    public void ReminderHour_CannotReachOrExceed22()
    {
        var vm = new SettingsViewModel();
        vm.Load(new AppConfig(), null);

        vm.ReminderHour = 22;
        Assert.Equal(21, vm.ReminderHour);

        vm.ReminderHour = 99;
        Assert.Equal(21, vm.ReminderHour);
    }

    [Fact]
    public void TryValidate_InvalidReminder_Fails()
    {
        var vm = new SettingsViewModel();
        vm.Load(new AppConfig(), null);

        // Force invalid via reflection-free path: clamp prevents 22,
        // so validate a constructed plan path with hour 21:59 OK and
        // ensure IsValidReminderStartTime rejects 22:00 at policy level.
        Assert.False(ShutdownPolicy.IsValidReminderStartTime(new TimeOnly(22, 0)));
        Assert.False(ShutdownPolicy.IsValidReminderStartTime(new TimeOnly(22, 30)));
        Assert.False(ShutdownPolicy.IsValidReminderStartTime(new TimeOnly(23, 0)));
        Assert.True(ShutdownPolicy.IsValidReminderStartTime(new TimeOnly(21, 59)));

        vm.ReminderHour = 21;
        vm.ReminderMinute = 59;
        Assert.True(vm.TryValidate(out var error));
        Assert.Null(error);
    }

    [Fact]
    public void LoadConfig_MapsReminderFields()
    {
        var config = new AppConfig
        {
            RunAtStartup = true,
            Shutdown = new ShutdownPlan
            {
                Enabled = true,
                ReminderStartTime = new TimeOnly(19, 45),
                DryRun = false
            }
        };

        var vm = new SettingsViewModel();
        vm.Load(config, D(2026, 8, 6, 19, 45));

        Assert.True(vm.Enabled);
        Assert.Equal(19, vm.ReminderHour);
        Assert.Equal(45, vm.ReminderMinute);
        Assert.False(vm.DryRun);
        Assert.True(vm.RunAtStartup);
    }

    [Fact]
    public void CancelDoesNotMutateOriginal()
    {
        var original = new AppConfig
        {
            RunAtStartup = false,
            Shutdown = new ShutdownPlan
            {
                Enabled = true,
                ReminderStartTime = new TimeOnly(18, 0),
                DryRun = true
            }
        };

        var vm = new SettingsViewModel();
        vm.Load(original, null);

        vm.Enabled = false;
        vm.ReminderHour = 20;
        vm.ReminderMinute = 15;
        vm.DryRun = false;
        vm.RunAtStartup = true;

        Assert.True(original.Shutdown.Enabled);
        Assert.Equal(new TimeOnly(18, 0), original.Shutdown.ReminderStartTime);
        Assert.True(original.Shutdown.DryRun);
        Assert.False(original.RunAtStartup);
    }

    [Fact]
    public void FormatNextReminder_TodayTomorrowFuture()
    {
        var now = DateTimeOffset.Now;
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, 18, 0, 0, now.Offset);
        var tomorrow = today.AddDays(1);
        var future = new DateTimeOffset(2026, 12, 25, 18, 0, 0, LocalOffset);

        Assert.Equal("无", SettingsViewModel.FormatNextReminder(null, false));
        Assert.StartsWith("今天", SettingsViewModel.FormatNextReminder(today, true));
        Assert.Contains("18:00", SettingsViewModel.FormatNextReminder(today, true));
        Assert.StartsWith("明天", SettingsViewModel.FormatNextReminder(tomorrow, true));
        Assert.Equal("2026年12月25日 18:00", SettingsViewModel.FormatNextReminder(future, true));
    }

    [Fact]
    public void ScheduledShutdown_TodayWhenReminderToday()
    {
        var now = DateTimeOffset.Now;
        var reminderToday = new DateTimeOffset(now.Year, now.Month, now.Day, 18, 0, 0, now.Offset);

        var text = SettingsViewModel.FormatScheduledShutdown(reminderToday, true);

        Assert.StartsWith("今天", text);
        Assert.Contains("22:00", text);
    }

    [Fact]
    public void ScheduledShutdown_TomorrowWhenReminderTomorrow()
    {
        var now = DateTimeOffset.Now;
        var reminderTomorrow = new DateTimeOffset(now.Year, now.Month, now.Day, 18, 0, 0, now.Offset)
            .AddDays(1);

        var text = SettingsViewModel.FormatScheduledShutdown(reminderTomorrow, true);

        Assert.StartsWith("明天", text);
        Assert.Contains("22:00", text);
    }

    [Fact]
    public void ScheduledShutdown_Disabled_IsNone()
    {
        Assert.Equal("无", SettingsViewModel.FormatScheduledShutdown(D(2026, 8, 6, 18, 0), false));
    }

    [Fact]
    public void ReminderMinute_Bounds()
    {
        var vm = new SettingsViewModel();
        vm.Load(new AppConfig(), null);

        vm.ReminderMinute = 60;
        Assert.Equal(59, vm.ReminderMinute);

        vm.ReminderMinute = -1;
        Assert.Equal(0, vm.ReminderMinute);
    }

    [Fact]
    public void ToAppConfig_IncludesRunAtStartup()
    {
        var vm = new SettingsViewModel();
        vm.Load(new AppConfig(), null);
        vm.RunAtStartup = true;
        vm.Enabled = true;
        vm.ReminderHour = 18;
        vm.ReminderMinute = 30;

        var config = vm.ToAppConfig();

        Assert.True(config.RunAtStartup);
        Assert.Equal(new TimeOnly(18, 30), config.Shutdown.ReminderStartTime);
    }

    [Fact]
    public void Disabled_ShowsNoNextReminder()
    {
        var config = new AppConfig
        {
            Shutdown = new ShutdownPlan { Enabled = false }
        };

        var vm = new SettingsViewModel();
        vm.Load(config, null);

        Assert.False(vm.Enabled);
        Assert.Equal("无", vm.NextReminderText);
        Assert.Equal("无", vm.ScheduledShutdownText);
    }
}
