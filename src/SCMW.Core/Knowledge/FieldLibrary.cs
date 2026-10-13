// src/SCMW.Core/Knowledge/FieldLibrary.cs
// The single source of truth for what a field IS. Built by FieldLibraryLoader.
namespace SCMW.Core.Knowledge;

/// <summary>An in-memory set of field definitions, looked up by kind and field name.
/// Kind and field-name matching is case-insensitive, matching the rest of the honesty engine.</summary>
public sealed class FieldLibrary
{
    private readonly Dictionary<string, List<FieldDefinition>> _byKind =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The definition for <paramref name="fieldName"/> in <paramref name="kind"/>,
    /// or null when the library does not describe it yet. First match wins.</summary>
    public FieldDefinition? Lookup(string kind, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(fieldName))
        {
            return null;
        }

        if (!_byKind.TryGetValue(kind.Trim(), out var fields))
        {
            return null;
        }

        var wanted = fieldName.Trim();
        foreach (var field in fields)
        {
            if (string.Equals(field.Name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return field;
            }
        }

        return null;
    }

    /// <summary>Every field the library describes for <paramref name="kind"/>, in file order.
    /// Empty when the kind has no described fields.</summary>
    public IReadOnlyList<FieldDefinition> ForKind(string kind)
    {
        if (!string.IsNullOrWhiteSpace(kind) && _byKind.TryGetValue(kind.Trim(), out var fields))
        {
            return fields;
        }

        return Array.Empty<FieldDefinition>();
    }

    /// <summary>Add one described field under <paramref name="kind"/>. Internal to the loader.</summary>
    internal void Add(string kind, FieldDefinition field)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return;
        }

        var key = kind.Trim();
        if (!_byKind.TryGetValue(key, out var fields))
        {
            fields = new List<FieldDefinition>();
            _byKind[key] = fields;
        }

        fields.Add(field);
    }
}
