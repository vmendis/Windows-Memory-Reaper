using System.Drawing;
using System.Windows.Threading;
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
    private DateTimeOffset? _automaticNextDue;
    private bool _menuOpen;
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public TrayIconController()
    {
        _countdownTimer.Tick += (_, _) => RefreshNextCleaningText();
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
        menu.Opened += (_, _) =>
        {
            _menuOpen = true;
            RefreshAutomaticState();
            _countdownTimer.Start();
        };
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            _countdownTimer.Stop();
        };
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
    /// Caches the automatic-cleaning state and the absolute next-cleanup time.
    /// The context-menu items are only touched when the menu is actually opened,
    /// because setting <c>IsChecked</c> on a checkable item of a never-opened
    /// menu triggers a native stack overflow inside USER32/SHELL32 on Windows 11.
    /// </summary>
    public void SetAutomaticState(bool enabled, int intervalMinutes, DateTimeOffset? nextDue)
    {
        _automaticEnabled = enabled;
        _automaticIntervalMinutes = intervalMinutes;
        _automaticNextDue = nextDue;
    }

    private void RefreshAutomaticState()
    {
        _automaticMenuItem.Header = _automaticEnabled
            ? $"Automatic Cleaning: on (every {_automaticIntervalMinutes} min)"
            : "Automatic Cleaning: off";
        RefreshNextCleaningText();
    }

    private void RefreshNextCleaningText()
    {
        if (_automaticEnabled && _menuOpen)
        {
            _nextCleaningMenuItem.Header = BuildNextCleaningText();
        }
    }

    private string BuildNextCleaningText()
    {
        if (!_automaticEnabled)
        {
            return "Next cleaning: disabled";
        }

        if (_automaticNextDue is not { } due)
        {
            return "Next cleaning: pending";
        }

        var remaining = due - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            return "Next cleaning: pending";
        }

        return $"Next cleaning: {FormatDelay(remaining)}";
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
