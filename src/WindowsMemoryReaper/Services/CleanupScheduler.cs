using System.IO;
using System.Threading.Tasks;

namespace WindowsMemoryReaper.Services;

/// <summary>
/// Manages the automatic-cleaning timer. The interval is measured from
/// completion of one cleaning operation, not the start (spec section 16).
/// </summary>
public sealed class CleanupScheduler : IDisposable
{
    private readonly Func<string?, string[], CancellationToken, Task<CleanupResult>> _clean;
    private readonly SettingsStore _settingsStore;
    private readonly object _gate = new();

    private System.Threading.Timer? _timer;
    private AppSettings _settings;
    private bool _cleanupInProgress;
    private bool _disposed;

    /// <summary>Fired after a cleanup cycle initiated by the scheduler completes.</summary>
    public event Action<CleanupResult>? CleanupCompleted;

    public CleanupScheduler(
        Func<string?, string[], CancellationToken, Task<CleanupResult>> clean,
        SettingsStore settingsStore,
        AppSettings settings)
    {
        _clean = clean ?? throw new ArgumentNullException(nameof(clean));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _settings = settings;
    }

    /// <summary>Current settings used by the scheduler.</summary>
    public AppSettings Settings
    {
        get { lock (_gate) return _settings; }
    }

    /// <summary>Absolute time of the next cleanup, when scheduled.</summary>
    public DateTimeOffset? NextDueTime { get; private set; }

    /// <summary>Reloads settings and restarts (or stops) the timer accordingly.</summary>
    public void Reload()
    {
        var settings = _settingsStore.Load();
        lock (_gate)
        {
            _settings = settings;
        }
        RestartTimerIfNeeded();
    }

    /// <summary>Starts the scheduler based on current settings.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed) return;
        }
        RestartTimerIfNeeded();
    }

    private void RestartTimerIfNeeded()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _timer?.Dispose();
            _timer = null;
            NextDueTime = null;

            var settings = _settings;
            if (!SettingsValid(settings))
            {
                return;
            }

            var interval = IntervalFromSettings(settings);
            NextDueTime = DateTimeOffset.Now + interval;
            _timer = new System.Threading.Timer(OnTimerFired, null, interval, Timeout.InfiniteTimeSpan);
        }
    }

    private async void OnTimerFired(object? state)
    {
        lock (_gate)
        {
            if (_disposed || _cleanupInProgress)
            {
                return;
            }
            _cleanupInProgress = true;
            // The countdown is suspended while a cleanup cycles; the next due
            // time is set again from the completion point (spec section 16).
            NextDueTime = null;
        }

        try
        {
            AppSettings settings;
            lock (_gate)
            {
                settings = _settings;
            }

            var result = await _clean(settings.RamMapPath, settings.GetEnabledOperations(), CancellationToken.None).ConfigureAwait(false);

            // Re-arm (and thus set the new due time) before notifying, so the
            // tray refresh reads the next due time rather than the stale one.
            ScheduleNextIfApplicable();
            CleanupCompleted?.Invoke(result);
        }
        finally
        {
            lock (_gate)
            {
                _cleanupInProgress = false;
            }
        }
    }

    private void ScheduleNextIfApplicable()
    {
        lock (_gate)
        {
            if (_disposed) return;

            var settings = _settings;
            if (!SettingsValid(settings))
            {
                _timer?.Dispose();
                _timer = null;
                NextDueTime = null;
                return;
            }

            var interval = IntervalFromSettings(settings);
            NextDueTime = DateTimeOffset.Now + interval;
            _timer?.Dispose();
            _timer = new System.Threading.Timer(OnTimerFired, null, interval, Timeout.InfiniteTimeSpan);
        }
    }

    private static bool SettingsValid(AppSettings settings)
        => settings.AutomaticCleaningEnabled
           && !string.IsNullOrWhiteSpace(settings.RamMapPath)
           && File.Exists(settings.RamMapPath);

    private static TimeSpan IntervalFromSettings(AppSettings settings)
        => TimeSpan.FromMinutes(Math.Max(1, settings.CleaningIntervalMinutes));

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}