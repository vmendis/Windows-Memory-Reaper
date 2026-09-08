namespace WindowsMemoryReaper.Services;

/// <summary>Outcome of a single cleanup operation.</summary>
public enum CleanupResultKind
{
    Completed,
    RamMapNotConfigured,
    RamMapNotFound,
    RamMapLaunchFailed,
    TimedOut,
    Failed,
    AlreadyRunning,

    /// <summary>The user declined the UAC prompt that spawns the elevated worker.</summary>
    ElevationDeclined,

    /// <summary>The elevated worker disconnected or failed during the operation.</summary>
    WorkerDisconnected,
}

/// <summary>Result reported after attempting a cleanup cycle.</summary>
public sealed record CleanupResult(CleanupResultKind Kind, int CompletedOperations, string? Detail);
