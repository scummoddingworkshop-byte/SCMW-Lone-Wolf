// src/SCMW.Core/Knowledge/FieldLibraryLoader.cs
// Reads the field library's JSON into a FieldLibrary. The JSON ships embedded in this
// assembly (src/SCMW.Core/Knowledge/library/*.json), so the library needs no file paths.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SCMW.Core.Knowledge;

/// <summary>Builds a <see cref="FieldLibrary"/> from JSON text or from the shipped files.</summary>
public static class FieldLibraryLoader
{
    // Embedded resource names use dots for path separators:
    // SCMW.Core.Knowledge.library.<file>.json
    private const string ResourceMarker = ".Knowledge.library.";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Parse one JSON document (an array of field entries) into a library.
    /// An empty or blank document yields an empty library.</summary>
    public static FieldLibrary Parse(string json)
    {
        var library = new FieldLibrary();
        ParseInto(json, library);
        return library;
    }

    /// <summary>Load every library JSON file shipped inside this assembly, merged into one
    /// library. Files are read in name order so the result is deterministic.</summary>
    public static FieldLibrary LoadDefault()
    {
        var library = new FieldLibrary();
        var assembly = typeof(FieldLibraryLoader).Assembly;

        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(ResourceMarker, StringComparison.Ordinal) &&
                           name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal);

        foreach (var resource in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            ParseInto(reader.ReadToEnd(), library);
        }

        return library;
    }

    private static void ParseInto(string json, FieldLibrary library)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var entries = JsonSerializer.Deserialize<List<FieldEntry>>(json, Options);
        if (entries is null)
        {
            return;
        }

        foreach (var entry in entries)
        {
            // A field with no kind or no name cannot be placed; skip it rather than
            // inventing one. The library must never crash on a bad entry.
            if (string.IsNullOrWhiteSpace(entry.Kind) || string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            var name = entry.Name.Trim();
            library.Add(entry.Kind.Trim(), new FieldDefinition(
                Name: name,
                Label: string.IsNullOrWhiteSpace(entry.Label) ? name : entry.Label.Trim(),
                Type: ParseType(entry.Type),
                Unit: entry.Unit?.Trim() ?? string.Empty,
                Risk: ParseRisk(entry.Risk),
                Writable: entry.Writable ?? false,
                Meaning: entry.Meaning?.Trim() ?? string.Empty));
        }
    }

    // An unrecognised type or risk reads as Unknown - never guessed.
    private static FieldType ParseType(string? value) =>
        Enum.TryParse<FieldType>(value?.Trim(), ignoreCase: true, out var type) ? type : FieldType.Unknown;

    private static Risk ParseRisk(string? value) =>
        Enum.TryParse<Risk>(value?.Trim(), ignoreCase: true, out var risk) ? risk : Risk.Unknown;

    private sealed record FieldEntry(
        string? Kind,
        string? Name,
        string? Label,
        string? Type,
        string? Unit,
        string? Risk,
        bool? Writable,
        string? Meaning);
}
