using System.Drawing;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using WindowsMemoryReaper.Services;

namespace WindowsMemoryReaper.Services;

/// <summary>
/// Owns the system tray icon and context menu. See spec sections 7, 8 and 19.
/// </summary>
public sealed class TrayIconController : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon = new();

    // Context menu items we need to update in place.
    private System.Windows.Controls.MenuItem _automaticMenuItem = new();
    private System.Windows.Controls.MenuItem _nextCleaningMenuItem = new();

    public TrayIconController()
    {
        BuildMenu();
    }

    public event Action? CleanNowRequested;
    public event Action? SettingsRequested;
    public event Action? ToggleAutomaticCleaningRequested;
    public event Action? ExitRequested;

    private void BuildMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var doubleClickMessage = new System.Windows.Controls.MenuItem
        {
            Header = "Windows Memory Reaper",
            IsEnabled = false,
        };
        menu.Items.Add(doubleClickMessage);
        menu.Items.Add(new System.Windows.Controls.Separator());

        var cleanNow = new System.Windows.Controls.MenuItem { Header = "Clean Now" };
        cleanNow.Click += (_, _) => CleanNowRequested?.Invoke();
        menu.Items.Add(cleanNow);

        _automaticMenuItem = new System.Windows.Controls.MenuItem { Header = "Automatic Cleaning", IsCheckable = true };
        _automaticMenuItem.Checked += (_, _) => ToggleAutomaticCleaningRequested?.Invoke();
        _automaticMenuItem.Unchecked += (_, _) => ToggleAutomaticCleaningRequested?.Invoke();
        menu.Items.Add(_automaticMenuItem);

        _nextCleaningMenuItem = new System.Windows.Controls.MenuItem
        {
            Header = "Automatic cleaning disabled",
            IsEnabled = false,
        };
        menu.Items.Add(_nextCleaningMenuItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var settings = new System.Windows.Controls.MenuItem { Header = "Settings..." };
        settings.Click += (_, _) => SettingsRequested?.Invoke();
        menu.Items.Add(settings);

        var exit = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(exit);

        _taskbarIcon.ContextMenu = menu;
        _taskbarIcon.ToolTipText = "Windows Memory Reaper";
        _taskbarIcon.LeftClickCommand = null;
        _taskbarIcon.TrayLeftMouseUp += (_, _) => SettingsRequested?.Invoke();
    }

    /// <summary>Sets the generated tray icon.</summary>
    public void SetIcon(Icon icon, string tooltip)
    {
        _taskbarIcon.Icon = icon;
        _taskbarIcon.ToolTipText = tooltip;
    }

    /// <summary>Updates the automatic-cleaning checkmark and countdown text.</summary>
    public void SetAutomaticState(bool enabled, TimeSpan? nextDelay)
    {
        _automaticMenuItem.IsChecked = enabled;
        _nextCleaningMenuItem.Header = !enabled
            ? "Automatic cleaning disabled"
            : nextDelay is { } d
                ? $"Next cleaning: {FormatDelay(d)}"
                : "Next cleaning: pending";
    }

    /// <summary>Shows a small balloon notification. Automatic notifications disabled by default.</summary>
    public void ShowNotification(string title, string message)
    {
        _taskbarIcon.ShowNotification(title, message, NotificationIcon.Info);
    }

    private static string FormatDelay(TimeSpan delay)
    {
        var minutes = (int)Math.Ceiling(delay.TotalMinutes);
        return minutes switch
        {
            <= 1 => "less than a minute",
            _ => $"{minutes} min",
        };
    }

    public void Dispose()
    {
        _taskbarIcon.Dispose();
    }
}
