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
    private bool _automaticEnabled;
    private int _automaticIntervalMinutes;
    private TimeSpan? _automaticNextDelay;

    public TrayIconController()
    {
        BuildMenu();
        SetIcon(TrayIconFactory.Create(TrayIconFactory.NormalColor), "Windows Memory Reaper");
        // In code-only (windowless) usage the TaskbarIcon is lazy and does not
        // register until Create() is forced. Without this no tray icon appears.
        _taskbarIcon.ForceCreate();
    }

    public event Action? CleanNowRequested;
    public event Action? SettingsRequested;
    public event Action? ToggleAutomaticCleaningRequested;
    public event Action? ExitRequested;

    private void BuildMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();
        menu.Opened += (_, _) => RefreshAutomaticState();
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

        _automaticMenuItem = new System.Windows.Controls.MenuItem { Header = "Automatic Cleaning" };
        _automaticMenuItem.Click += (_, _) => ToggleAutomaticCleaningRequested?.Invoke();
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

    /// <summary>
    /// Caches the automatic-cleaning state and countdown text. The context-menu
    /// items are only touched when the menu is actually opened, because setting
    /// <c>IsChecked</c> on a checkable item of a never-opened menu triggers a
    /// native stack overflow inside USER32/SHELL32 on Windows 11.
    /// </summary>
    public void SetAutomaticState(bool enabled, int intervalMinutes, TimeSpan? nextDelay)
    {
        _automaticEnabled = enabled;
        _automaticIntervalMinutes = intervalMinutes;
        _automaticNextDelay = nextDelay;
    }

    private void RefreshAutomaticState()
    {
        _automaticMenuItem.Header = _automaticEnabled
            ? $"Automatic Cleaning: on (every {_automaticIntervalMinutes} min)"
            : "Automatic Cleaning: off";
        _nextCleaningMenuItem.Header = !_automaticEnabled
            ? "Next cleaning: disabled"
            : _automaticNextDelay is { } d
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
