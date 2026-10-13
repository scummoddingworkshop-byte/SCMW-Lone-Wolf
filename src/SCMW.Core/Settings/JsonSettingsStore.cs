using System.Text.Json;
using System.Text.Json.Serialization;
using SCMW.Core.Logging;

namespace SCMW.Core.Settings;

/// <summary>
/// JSON file settings store: <baseDir>/settings.json, camelCase properties.
/// Corrupt-safe: unreadable/unparseable state falls back to defaults, never throws.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    private readonly string _filePath;

    public JsonSettingsStore(string baseDir)
    {
        _filePath = Path.Combine(baseDir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new AppSettings(null, null, null);

            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
            var settings = loaded ?? new AppSettings(null, null, null);

            // Whoever loads settings seeds logger redaction (never auto-load inside Log).
            if (!string.IsNullOrEmpty(settings.AesKey))
                Log.RegisterSecret(settings.AesKey);

            return settings;
        }
        catch (JsonException)
        {
            Log.Warn("settings corrupt, using defaults");
            return new AppSettings(null, null, null);
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            // A locked or unreadable file is not "corrupt" — say so honestly and use defaults.
            Log.Warn($"settings could not be read, using defaults ({ex.GetType().Name})");
            return new AppSettings(null, null, null);
        }
    }

    /// <summary>
    /// Writes the settings atomically. Never throws: a locked/full disk is logged and the previous
    /// file is left untouched (this is called on every keystroke and on window close).
    /// </summary>
    public void Save(AppSettings s)
    {
        var temp = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            using (var fs = File.Create(temp))
                JsonSerializer.Serialize(fs, s, Options);
            File.Move(temp, _filePath, overwrite: true);
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            TryDelete(temp);
            Log.Warn($"settings could not be saved - the previous file is unchanged ({ex.GetType().Name})");
        }
    }

    private static bool IsExpectedIoFailure(Exception ex) =>
        ex is JsonException or IOException or UnauthorizedAccessException
            or System.Security.SecurityException or NotSupportedException or ArgumentException;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (IsExpectedIoFailure(ex)) { /* best effort only */ }
    }
}
