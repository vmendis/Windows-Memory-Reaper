using System.IO;
using System.Threading.Tasks;

namespace WindowsMemoryReaper.Services;

/// <summary>
/// Manages the automatic-cleaning timer. The interval is measured from
/// completion of one cleaning operation, not the start (spec section 16).
/// </summary>
public sealed class CleanupScheduler : IDisposable
{
    private readonly RamMapService _ramMap;
    private readonly SettingsStore _settingsStore;
    private readonly object _gate = new();

    private System.Threading.Timer? _timer;
    private AppSettings _settings;
    private bool _cleanupInProgress;
    private bool _disposed;

    /// <summary>Fired after a cleanup cycle initiated by the scheduler completes.</summary>
    public event Action<CleanupResult>? CleanupCompleted;

    public CleanupScheduler(RamMapService ramMap, SettingsStore settingsStore, AppSettings settings)
    {
        _ramMap = ramMap ?? throw new ArgumentNullException(nameof(ramMap));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _settings = settings;
    }

    /// <summary>Current settings used by the scheduler.</summary>
    public AppSettings Settings
    {
        get { lock (_gate) return _settings; }
    }

    /// <summary>The interval in minutes, if automatic cleaning is enabled and valid.</summary>
    public TimeSpan? NextDelay { get; private set; }

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
            NextDelay = null;

            var settings = _settings;
            if (!settings.AutomaticCleaningEnabled)
            {
                return;
            }

            var path = settings.RamMapPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                // RAMMap unavailable: automatic cleaning disabled until corrected (spec 15).
                return;
            }

            var interval = TimeSpan.FromMinutes(Math.Max(1, settings.CleaningIntervalMinutes));
            NextDelay = interval;
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
        }

        try
        {
            AppSettings settings;
            lock (_gate)
            {
                settings = _settings;
            }

            var result = await _ramMap.RunCleanupAsync(settings).ConfigureAwait(false);
            CleanupCompleted?.Invoke(result);

            lock (_gate)
            {
                if (result.Kind == CleanupResultKind.Completed)
                {
                    // Schedule next run only if auto-clean is still desired and RAMMap still valid.
                    scheduleNext(settings, resultKind: CleanupResultKind.Completed);
                }
                else if (NeedsRetry(result.Kind))
                {
                    // Keep trying on a fixed backoff rather than hammering RAMMap (spec 15);
                    // retry at the normal interval.
                    scheduleNext(settings, resultKind: result.Kind);
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _cleanupInProgress = false;
            }
        }
    }

    private void scheduleNext(AppSettings settings, CleanupResultKind resultKind)
    {
        if (_disposed || !settings.AutomaticCleaningEnabled)
        {
            return;
        }

        var path = settings.RamMapPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            NextDelay = null;
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, settings.CleaningIntervalMinutes));
        NextDelay = interval;
        _timer?.Dispose();
        _timer = new System.Threading.Timer(OnTimerFired, null, interval, Timeout.InfiniteTimeSpan);
    }

    private static bool NeedsRetry(CleanupResultKind kind)
        => kind is CleanupResultKind.TimedOut
            or CleanupResultKind.Failed
            or CleanupResultKind.RamMapLaunchFailed;

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
