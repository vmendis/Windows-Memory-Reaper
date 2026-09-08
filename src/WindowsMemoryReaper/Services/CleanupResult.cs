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
}

/// <summary>Result reported after attempting a cleanup cycle.</summary>
public sealed record CleanupResult(CleanupResultKind Kind, int CompletedOperations, string? Detail);
