using System.Windows;
using Wpf.Ui.Controls;

namespace ShutdownGuard.Services;

public static class Dialogs
{
    public static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string content,
        string primaryText = "OK",
        string closeText = "Cancel",
        ControlAppearance primaryAppearance = ControlAppearance.Primary)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            PrimaryButtonAppearance = primaryAppearance,
            Owner = owner
        };

        var result = await box.ShowDialogAsync();
        return result == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    public static Task NotifyAsync(
        Window owner,
        string title,
        string content,
        string closeText = "OK")
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = content,
            CloseButtonText = closeText,
            IsPrimaryButtonEnabled = false,
            IsSecondaryButtonEnabled = false,
            Owner = owner
        };
        return box.ShowDialogAsync();
    }
}
