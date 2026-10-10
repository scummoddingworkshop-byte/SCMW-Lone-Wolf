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
        catch (IOException)
        {
            Log.Warn("settings corrupt, using defaults");
            return new AppSettings(null, null, null);
        }
    }

    public void Save(AppSettings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        // Write to a temp file, then swap it in, so a crash mid-write can never leave a half file.
        var temp = _filePath + ".tmp";
        using (var fs = File.Create(temp))
            JsonSerializer.Serialize(fs, s, Options);
        File.Move(temp, _filePath, overwrite: true);
    }
}
