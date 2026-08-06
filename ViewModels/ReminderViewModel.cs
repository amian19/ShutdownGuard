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
                IsVisiblePhase = true;
                break;
            default:
                StatusText = "";
                CanCancel = false;
                IsVisiblePhase = false;
                break;
        }

        ShutdownAtText = snapshot.FixedShutdownAt is { } at
            ? $"计划关机时间：{at:HH:mm}"
            : $"计划关机时间：{ShutdownPolicy.FixedShutdownTime:HH:mm}";

        RemainingText = FormatRemaining(snapshot);
        DryRunText = snapshot.DryRun
            ? "安全测试模式：开启（最终执行阶段不会真正关机）。"
            : "警告：最终执行阶段将真正关闭 Windows。";
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
        var totalMinutes = (int)Math.Floor(ts.TotalMinutes);
        if (totalMinutes < 1)
            return $"{Math.Max(0, (int)Math.Floor(ts.TotalSeconds))} 秒";

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours > 0 && minutes > 0) return $"{hours} 小时 {minutes} 分钟";
        if (hours > 0) return $"{hours} 小时";
        return $"{minutes} 分钟";
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
