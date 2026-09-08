using System.IO;
using System.Windows;
using System.Windows.Threading;
using WindowsMemoryReaper.Services;

namespace WindowsMemoryReaper.Services;

/// <summary>
/// Coordinates the tray icon, settings window, scheduler and RAMMap service.
/// </summary>
public sealed class AppController : IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly RamMapService _ramMap;
    private readonly TrayIconController _tray;
    private readonly CleanupScheduler _scheduler;
    private AppSettings _settings;

    private SettingsWindow? _settingsWindow;
    private bool _disposed;
    private bool _exitRequested;
    private CancellationTokenSource? _exitCts;

    public AppController()
    {
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        _ramMap = new RamMapService();
        _tray = new TrayIconController();
        _scheduler = new CleanupScheduler(_ramMap, _settingsStore, _settings);

        WireEvents();
        ApplyIcon(AppStatus.Normal);
        UpdateTrayState();

        _scheduler.Start();
    }

    private void WireEvents()
    {
        _tray.CleanNowRequested += () => _ = CleanNowAsync();
        _tray.SettingsRequested += OpenSettings;
        _tray.ExitRequested += RequestExit;
        _tray.ToggleAutomaticCleaningRequested += ToggleAutomaticCleaning;
        _scheduler.CleanupCompleted += OnScheduledCleanupCompleted;
    }

    /// <summary>Updates the in-memory settings without persisting (used by the settings window).</summary>
    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Public entry point for a manual clean (from tray or settings).</summary>
    public async Task OnCleanNowAsync()
    {
        ApplyIcon(AppStatus.Cleaning);
        var result = await _ramMap.RunCleanupAsync(_settings).ConfigureAwait(true);
        HandleCleanupResult(result);
    }

    private async Task CleanNowAsync()
    {
        var result = await _ramMap.RunCleanupAsync(_settings).ConfigureAwait(true);
        HandleCleanupResult(result);
    }

    private void HandleCleanupResult(CleanupResult result)
    {
        switch (result.Kind)
        {
            case CleanupResultKind.Completed:
                ApplyIcon(AppStatus.Normal);
                _tray.ShowNotification("Memory Reaper", "Memory cleanup completed.");
                break;

            case CleanupResultKind.RamMapNotConfigured:
            case CleanupResultKind.RamMapNotFound:
                ApplyIcon(AppStatus.Warning);
                ShowCleanupError("RAMMap64.exe could not be found. Please check the RAMMap location in Settings.");
                break;

            case CleanupResultKind.RamMapLaunchFailed:
            case CleanupResultKind.Failed:
            case CleanupResultKind.TimedOut:
                ApplyIcon(AppStatus.Error);
                ShowCleanupError("Memory cleanup could not be completed.");
                break;

            case CleanupResultKind.AlreadyRunning:
                break;
        }

        if (_exitRequested)
        {
            CompleteExit();
        }
    }

    private void OnScheduledCleanupCompleted(CleanupResult result)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ApplyIcon(result.Kind == CleanupResultKind.Completed ? AppStatus.Normal : AppStatus.Error);
            UpdateTrayState();
        });
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true, IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(this, _settings);
        _settingsWindow.Show();
    }

    public void OnSettingsSaved(AppSettings newSettings)
    {
        _settings = newSettings;
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(_settingsWindow, ex.Message, "Windows Memory Reaper",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _scheduler.Reload();
        ApplyIcon(string.IsNullOrWhiteSpace(_settings.RamMapPath) || File.Exists(_settings.RamMapPath)
            ? AppStatus.Normal
            : AppStatus.Warning);
        UpdateTrayState();
    }

    private void ToggleAutomaticCleaning()
    {
        _settings.AutomaticCleaningEnabled = !_settings.AutomaticCleaningEnabled;
        OnSettingsSaved(_settings);
    }

    private void UpdateTrayState()
    {
        _tray.SetAutomaticState(_settings.AutomaticCleaningEnabled, _scheduler.NextDelay);
    }

    private void ApplyIcon(AppStatus status)
    {
        var color = status switch
        {
            AppStatus.Warning => TrayIconFactory.WarningColor,
            AppStatus.Cleaning => TrayIconFactory.CleaningColor,
            AppStatus.Error => TrayIconFactory.ErrorColor,
            _ => TrayIconFactory.NormalColor,
        };

        _tray.SetIcon(TrayIconFactory.Create(color), TextFor(status));
    }

    private static string TextFor(AppStatus status) => status switch
    {
        AppStatus.Warning => "Windows Memory Reaper — RAMMap unavailable",
        AppStatus.Cleaning => "Windows Memory Reaper — cleaning",
        AppStatus.Error => "Windows Memory Reaper — last cleanup failed",
        _ => "Windows Memory Reaper",
    };

    private void ShowCleanupError(string message)
    {
        MessageBox.Show(_settingsWindow, message, "Windows Memory Reaper",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void RequestExit()
    {
        if (_exitRequested) return;
        _exitRequested = true;

        _scheduler.Dispose();
        _exitCts = new CancellationTokenSource();
        _exitCts.CancelAfter(TimeSpan.FromSeconds(10));

        if (_ramMap.IsRunning)
        {
            _ = WaitForExitAsync();
        }
        else
        {
            CompleteExit();
        }
    }

    private async Task WaitForExitAsync()
    {
        try
        {
            while (_ramMap.IsRunning)
            {
                await Task.Delay(100, _exitCts!.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout waiting; proceed with exit anyway.
        }

        await Application.Current.Dispatcher.InvokeAsync(CompleteExit, DispatcherPriority.Background);
    }

    private void CompleteExit()
    {
        if (_disposed) return;
        _disposed = true;

        _scheduler.Dispose();
        _settingsWindow?.Close();
        _tray.Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scheduler.Dispose();
        _tray.Dispose();
    }

    private enum AppStatus
    {
        Normal,
        Warning,
        Cleaning,
        Error,
    }
}