using System.Text.Json.Serialization;

namespace WindowsMemoryReaper.Services;

/// <summary>
/// User-configurable settings, persisted as JSON beside the executable.
/// See spec section 9.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Full path to RAMMap64.exe. Empty when not configured.</summary>
    public string RamMapPath { get; set; } = string.Empty;

    /// <summary>Whether automatic cleaning is enabled. Default disabled.</summary>
    public bool AutomaticCleaningEnabled { get; set; }

    /// <summary>Interval (in minutes) between automatic cleanups.</summary>
    public int CleaningIntervalMinutes { get; set; } = DefaultIntervalMinutes;

    public const int DefaultIntervalMinutes = 30;

    /// <summary>Enable Empty Working Sets (-Ew). Default enabled.</summary>
    public bool OperationEmptyWorkingSets { get; set; } = true;

    /// <summary>Enable Empty System Working Set (-Es). Default enabled.</summary>
    public bool OperationEmptySystemWorkingSet { get; set; } = true;

    /// <summary>Enable Empty Modified Page List (-Em). Default enabled.</summary>
    public bool OperationEmptyModifiedPageList { get; set; } = true;

    /// <summary>Enable Empty Standby List (-Et). Default enabled.</summary>
    public bool OperationEmptyStandbyList { get; set; } = true;

    /// <summary>Enable Empty Priority 0 Standby List (-E0). Default enabled.</summary>
    public bool OperationEmptyPriority0StandbyList { get; set; } = true;

    /// <summary>
    /// Returns enabled RAMMap operation switches in canonical order (-Ew, -Es, -Em, -Et, -E0).
    /// </summary>
    public string[] GetEnabledOperations()
    {
        var list = new List<string>(5);
        if (OperationEmptyWorkingSets) list.Add("-Ew");
        if (OperationEmptySystemWorkingSet) list.Add("-Es");
        if (OperationEmptyModifiedPageList) list.Add("-Em");
        if (OperationEmptyStandbyList) list.Add("-Et");
        if (OperationEmptyPriority0StandbyList) list.Add("-E0");
        return [.. list];
    }

    /// <summary>Creates the recommended first-run defaults (spec section 26).</summary>
    public static AppSettings CreateDefaults() => new();
}

/// <summary>
/// <see cref="JsonSerializerContext"/> for <see cref="AppSettings"/>.
/// Source-generated to remain trimming / Native AOT friendly.
/// </summary>
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext
{
}
