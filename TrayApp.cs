using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ShutdownGuard.Models;
using ShutdownGuard.Services;
using Hardcodet.Wpf.TaskbarNotification;
using Wpf.Ui.Controls;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;
using StackPanel = System.Windows.Controls.StackPanel;
using Orientation = System.Windows.Controls.Orientation;

namespace ShutdownGuard;

public sealed class TrayApp : IDisposable
{
    private readonly ConfigStore _store = new();
    private TaskbarIcon? _trayIcon;
    private AppConfig _config = new();

    private static readonly BitmapImage _iconSource =
        new(new Uri("pack://application:,,,/Assets/IdlePulse.ico", UriKind.Absolute));

    public void Start()
    {
        _config = _store.Load();

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "ShutdownGuard",
            IconSource = _iconSource,
            ContextMenu = BuildContextMenu()
        };
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        // Header
        var header = new TextBlock
        {
            Text = "ShutdownGuard",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13
        };
        var headerItem = new MenuItem
        {
            Header = header,
            IsEnabled = false,
            StaysOpenOnClick = true
        };
        menu.Items.Add(headerItem);

        menu.Items.Add(new Separator());

        // Exit
        var exitItem = new MenuItem
        {
            Header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new SymbolIcon { Symbol = SymbolRegular.SignOut24, FontSize = 14, Margin = new Thickness(0, 0, 8, 0) },
                    new TextBlock { Text = "Exit", VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        exitItem.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
    }
}
