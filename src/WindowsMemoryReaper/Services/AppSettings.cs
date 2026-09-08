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
