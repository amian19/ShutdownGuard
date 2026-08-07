using System.ComponentModel;
using System.Runtime.CompilerServices;
using ShutdownGuard.Core;
using ShutdownGuard.Models;

namespace ShutdownGuard.ViewModels;

/// <summary>
/// Presentation-only ViewModel for ReminderWindow.
/// </summary>
public sealed class ReminderViewModel : INotifyPropertyChanged
{
    private string _statusText = "";
    private string _remainingText = "";
    private string _shutdownAtText = "";
    private string _dryRunText = "";
    private bool _canCancel;
    private bool _isVisiblePhase;

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public string RemainingText
    {
        get => _remainingText;
        private set { _remainingText = value; OnPropertyChanged(); }
    }

    public string ShutdownAtText
    {
        get => _shutdownAtText;
        private set { _shutdownAtText = value; OnPropertyChanged(); }
    }

    public string DryRunText
    {
        get => _dryRunText;
        private set { _dryRunText = value; OnPropertyChanged(); }
    }

    public bool CanCancel
    {
        get => _canCancel;
        private set { _canCancel = value; OnPropertyChanged(); }
    }

    /// <summary>True while the reminder window should remain available.</summary>
    public bool IsVisiblePhase
    {
        get => _isVisiblePhase;
        private set { _isVisiblePhase = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(ReminderSessionSnapshot snapshot)
    {
        switch (snapshot.Phase)
        {
            case ReminderSessionPhase.Active:
                StatusText = "今天已进入提醒期";
                CanCancel = true;
                IsVisiblePhase = true;
                break;
            case ReminderSessionPhase.Cancelled:
                StatusText = "已取消本次自动关机";
                CanCancel = false;
                IsVisiblePhase = true;
                break;
            case ReminderSessionPhase.Due:
                StatusText = "已到达计划关机时间";
                CanCancel = false;
                IsVisiblePhase = false;
                break;
            default:
                StatusText = "";
                CanCancel = false;
                IsVisiblePhase = false;
                break;
        }

        ShutdownAtText = snapshot.FixedShutdownAt is { } at
            ? $"计划关机时间：{at:HH:mm}"
            : $"计划关机时间：{ShutdownPolicy.EffectiveFixedShutdownTime:HH:mm}";

        RemainingText = FormatRemaining(snapshot);
        DryRunText = snapshot.DryRun
            ? "安全测试模式：开启（最终执行阶段不会真正关机）。"
            : "注意：到点将强制关机，请提前保存工作。";
    }

    public static string FormatRemaining(ReminderSessionSnapshot snapshot)
    {
        if (snapshot.Phase == ReminderSessionPhase.Cancelled)
            return "本次不会关机";

        if (snapshot.Phase == ReminderSessionPhase.Due)
            return "剩余时间：0 秒";

        if (snapshot.RemainingUntilShutdown is not { } remaining)
            return "";

        if (remaining <= TimeSpan.Zero)
            return "剩余时间：0 秒";

        return $"剩余时间：{FormatChineseDuration(remaining)}";
    }

    internal static string FormatChineseDuration(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero)
            ts = TimeSpan.Zero;

        // Use clock components (not Floor(TotalMinutes)) so 1m59s shows as
        // "1 分钟 59 秒" instead of truncating to "1 分钟".
        var hours = (int)Math.Floor(ts.TotalHours);
        var minutes = ts.Minutes;
        var seconds = ts.Seconds;

        if (hours > 0)
        {
            if (minutes > 0) return $"{hours} 小时 {minutes} 分钟";
            return $"{hours} 小时";
        }

        if (minutes > 0)
        {
            if (seconds > 0) return $"{minutes} 分钟 {seconds} 秒";
            return $"{minutes} 分钟";
        }

        return $"{seconds} 秒";
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
