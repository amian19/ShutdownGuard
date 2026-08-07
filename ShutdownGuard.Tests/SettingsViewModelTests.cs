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
    public void Defaults_EnabledTrue_Reminder1800_DryRunFalse()
    {
        var config = new AppConfig();
        var vm = new SettingsViewModel();
        vm.Load(config, null);

        Assert.True(vm.Enabled);
        Assert.Equal(18, vm.ReminderHour);
        Assert.Equal(0, vm.ReminderMinute);
        Assert.False(vm.DryRun);
        Assert.True(vm.RunAtStartup);
        Assert.Equal("22:00", vm.FixedShutdownDisplay);
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
    public void ReminderHour_CanReach23_ButValidateAgainstShutdown()
    {
        var vm = new SettingsViewModel();
        vm.Load(new AppConfig(), null);

        vm.ReminderHour = 22;
        Assert.Equal(22, vm.ReminderHour);
        Assert.False(vm.TryValidate(out _)); // default shutdown 22:00

        vm.ReminderHour = 99;
        Assert.Equal(23, vm.ReminderHour);
    }

    [Fact]
    public void TryValidate_RejectsReminderAtOrAfterFixed2200()
    {
        var vm = new SettingsViewModel();
        vm.Load(new AppConfig(), null);
        vm.ReminderHour = 22;
        vm.ReminderMinute = 0;

        Assert.False(vm.TryValidate(out var error));
        Assert.Contains("22:00", error);
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
        vm.RunAtStartup = false;

        Assert.True(original.Shutdown.Enabled);
        Assert.Equal(new TimeOnly(18, 0), original.Shutdown.ReminderStartTime);
        Assert.True(original.Shutdown.DryRun);
        Assert.False(original.RunAtStartup);
        // ViewModel ignores attempts to turn autostart / global enable off.
        Assert.True(vm.RunAtStartup);
        Assert.True(vm.Enabled);
        Assert.True(vm.TodayWillShutdown);
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
    public void ScheduledShutdown_BeforeTodayShutdown_ShowsToday_EvenIfNextReminderTomorrow()
    {
        var now = DateTimeOffset.Now;
        // After reminder already fired, scheduler NextReminder is often tomorrow —
        // but "本次计划关机" must still be today while before today's fixed clock.
        var reminderTomorrow = new DateTimeOffset(now.Year, now.Month, now.Day, 18, 0, 0, now.Offset)
            .AddDays(1);

        var text = SettingsViewModel.FormatScheduledShutdown(reminderTomorrow, true);

        if (now.TimeOfDay < ShutdownPolicy.FixedShutdownTime.ToTimeSpan())
        {
            Assert.StartsWith("今天", text);
            Assert.Contains("22:00", text);
        }
        else
        {
            Assert.StartsWith("明天", text);
            Assert.Contains("22:00", text);
        }
    }

    [Fact]
    public void ScheduledShutdown_TodayCancelled_ShowsCancelledLabel()
    {
        var now = DateTimeOffset.Now;
        var reminderTomorrow = new DateTimeOffset(now.Year, now.Month, now.Day, 18, 0, 0, now.Offset)
            .AddDays(1);

        var text = SettingsViewModel.FormatScheduledShutdown(reminderTomorrow, true, todayCancelled: true);

        Assert.Contains("今天已取消", text);
        Assert.StartsWith("明天", text);
    }

    [Fact]
    public void ScheduledShutdown_TodayCancelled_NextStillToday_ShowsCancelledOnly()
    {
        var now = DateTimeOffset.Now;
        var reminderToday = new DateTimeOffset(now.Year, now.Month, now.Day, 18, 0, 0, now.Offset);

        Assert.Equal(
            "今天已取消",
            SettingsViewModel.FormatScheduledShutdown(reminderToday, true, todayCancelled: true));
        Assert.Equal(
            "今天已取消（不应再提醒）",
            SettingsViewModel.FormatNextReminder(reminderToday, true, todayCancelled: true));
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
        vm.RunAtStartup = false;
        vm.Enabled = true;
        vm.ReminderHour = 18;
        vm.ReminderMinute = 30;

        var config = vm.ToAppConfig();

        Assert.True(config.RunAtStartup);
        Assert.Equal(new TimeOnly(18, 30), config.Shutdown.ReminderStartTime);
    }

    [Fact]
    public void Load_IgnoresDisabledFlag_AlwaysShowsTodayWillShutdown()
    {
        var config = new AppConfig
        {
            Shutdown = new ShutdownPlan { Enabled = false }
        };

        var vm = new SettingsViewModel();
        vm.Load(config, null);

        Assert.True(vm.Enabled);
        Assert.True(vm.TodayWillShutdown);
        Assert.Equal("无", vm.NextReminderText);
        Assert.Contains("22:00", vm.ScheduledShutdownText);
    }

    [Fact]
    public void Load_TodayCancelled_ShowsSkipTodayStatus()
    {
        var vm = new SettingsViewModel();
        vm.Load(new AppConfig(), D(2026, 8, 7, 18, 0), todayCancelled: true);

        Assert.False(vm.TodayWillShutdown);
    }
}
