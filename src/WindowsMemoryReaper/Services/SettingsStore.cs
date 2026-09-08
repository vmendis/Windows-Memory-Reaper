using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsMemoryReaper.Services;

/// <summary>
/// Loads and saves application settings as JSON beside the executable,
/// preserving the portable behaviour required by spec section 9.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        TypeInfoResolver = AppSettingsJsonContext.Default,
    };

    private readonly string _settingsPath;

    public SettingsStore()
        : this(DefaultSettingsPath())
    {
    }

    public SettingsStore(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    /// <summary>Path to the JSON settings file, beside the executable.</summary>
    public string SettingsPath => _settingsPath;

    public static string DefaultSettingsPath()
        => Path.Combine(AppContext.BaseDirectory, "WindowsMemoryReaper.json");

    /// <summary>
    /// Loads settings. Returns first-run defaults when the file is missing.
    /// Throws <see cref="InvalidOperationException"/> when the file exists but
    /// cannot be read or parsed, so the caller can surface a clear error.
    /// </summary>
    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return AppSettings.CreateDefaults();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
            return settings ?? AppSettings.CreateDefaults();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException(
                $"Could not read settings file '{_settingsPath}'.", ex);
        }
    }

    /// <summary>
    /// Saves settings, writing the file if it does not exist.
    /// Throws <see cref="InvalidOperationException"/> when the directory is
    /// read-only or not writable, rather than silently storing elsewhere.
    /// </summary>
    public void Save(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                throw new IOException($"Directory '{directory}' does not exist.");
            }

            var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Could not save settings to '{_settingsPath}'. " +
                "The executable directory may be read-only.", ex);
        }
    }
}
