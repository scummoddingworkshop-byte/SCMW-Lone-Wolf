using System.Globalization;
using SCMW.Core.Content;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Pak;

/// <summary>
/// Reads the values hidden inside structs — and inside lists of structs (a recipe's ingredient
/// slots) — using the same library the build writer uses (UAssetAPI). Given a package's raw files,
/// it parses them and hands a node tree to <see cref="NestedFieldWalker"/>. Links and lists are
/// resolved to plain names where it can (a recipe's Product, an ingredient's accepted tags) and
/// shown locked. Never throws; any trouble yields no rows.
/// </summary>
internal static class NestedValueReader
{
    /// <summary>How many entries of one list we look at, so a huge array cannot spin.</summary>
    private const int MaxArrayElements = 64;

    /// <summary>How deep the parser recurses, so a pathological package cannot blow the stack.</summary>
    private const int MaxDepth = 12;

    internal static IReadOnlyList<AssetField> Read(IReadOnlyDictionary<string, byte[]>? files)
    {
        if (files is null || files.Count == 0)
            return Array.Empty<AssetField>();

        var uassetKey = files.Keys.FirstOrDefault(
            k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase));
        if (uassetKey is null)
            return Array.Empty<AssetField>();

        // UAssetAPI opens the .uasset and reads the sibling .uexp by path, so the pair is laid
        // down in a scratch folder first (the same proven route the build writer's tests use).
        var scratch = Path.Combine(Path.GetTempPath(), "scmw-nested-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(scratch);
            foreach (var pair in files)
                File.WriteAllBytes(Path.Combine(scratch, pair.Key), pair.Value);

            UAsset asset;
            try
            {
                asset = new UAsset(Path.Combine(scratch, uassetKey), EngineVersion.VER_UE4_27);
            }
            catch (Exception)
            {
                return Array.Empty<AssetField>();
            }

            var nodes = new List<NestedProperty>();
            foreach (var export in asset.Exports.OfType<NormalExport>())
            {
                foreach (var property in export.Data)
                {
                    // One property we cannot read must not blank the whole package.
                    try
                    {
                        var node = ToNode(property, property.Name.ToString(), asset, 0);
                        if (node is not null) nodes.Add(node);
                    }
                    catch (Exception)
                    {
                        // Skip a property we cannot read; the rest of the package still reads.
                    }
                }
            }

            return NestedFieldWalker.Walk(nodes);
        }
        catch (Exception)
        {
            return Array.Empty<AssetField>();
        }
        finally
        {
            try { Directory.Delete(scratch, true); } catch (Exception) { /* best-effort cleanup */ }
        }
    }

    /// <summary>Turns one library property into a node (structs and lists recursed), or null.</summary>
    private static NestedProperty? ToNode(PropertyData property, string name, UAsset asset, int depth)
    {
        if (string.IsNullOrEmpty(name) || depth > MaxDepth) return null;

        if (property is StructPropertyData structure && structure.Value is { Count: > 0 })
        {
            var children = new List<NestedProperty>();
            foreach (var child in structure.Value)
            {
                var node = ToNode(child, child.Name.ToString(), asset, depth + 1);
                if (node is not null) children.Add(node);
            }
            return children.Count > 0 ? new NestedProperty(name, HintOf(property), null, children) : null;
        }

        if (property is ArrayPropertyData array && array.Value is { Length: > 0 })
        {
            var elements = new List<NestedProperty>();
            for (var i = 0; i < Math.Min(array.Value.Length, MaxArrayElements); i++)
            {
                var node = ToNode(array.Value[i], "[" + i.ToString(CultureInfo.InvariantCulture) + "]", asset, depth + 1);
                if (node is not null) elements.Add(node);
            }
            return elements.Count > 0 ? new NestedProperty(name, HintOf(property), null, elements) : null;
        }

        // A link (an object reference) shows the name of what it points at; it is offered for
        // editing only where the safety rule allows (an ingredient's accepted tags).
        if (property is ObjectPropertyData or SoftObjectPropertyData)
            return new NestedProperty(name, HintOf(property), Reference(property, asset), Array.Empty<NestedProperty>());

        if (TryText(property, out var text))
            return new NestedProperty(name, HintOf(property), text, Array.Empty<NestedProperty>());

        return null;
    }

    /// <summary>The UE type the honesty engine reads. An enum-backed byte is a named choice.</summary>
    private static string HintOf(PropertyData property)
    {
        if (property is EnumPropertyData) return "EnumProperty";
        if (property is BytePropertyData b && b.ByteType != BytePropertyType.Byte) return "EnumProperty";
        return property.PropertyType?.ToString() ?? string.Empty;
    }

    /// <summary>The plain text of a scalar property, or false for a kind we do not show yet.</summary>
    private static bool TryText(PropertyData property, out string text)
    {
        switch (property)
        {
            case FloatPropertyData f: text = f.Value.ToString(CultureInfo.InvariantCulture); return true;
            case DoublePropertyData d: text = d.Value.ToString(CultureInfo.InvariantCulture); return true;
            case IntPropertyData i: text = i.Value.ToString(CultureInfo.InvariantCulture); return true;
            case Int64PropertyData l: text = l.Value.ToString(CultureInfo.InvariantCulture); return true;
            case BoolPropertyData b: text = b.Value ? "True" : "False"; return true;
            case BytePropertyData by:
                text = by.ByteType == BytePropertyType.Byte
                    ? by.Value.ToString(CultureInfo.InvariantCulture)
                    : by.EnumValue?.ToString() ?? string.Empty;
                return true;
            case EnumPropertyData e: text = e.Value?.ToString() ?? string.Empty; return true;
            case StrPropertyData s: text = s.Value?.ToString() ?? string.Empty; return true;
            case TextPropertyData t: text = t.Value?.ToString() ?? string.Empty; return true;
            case NamePropertyData n: text = n.Value?.ToString() ?? string.Empty; return true;
            default: text = string.Empty; return false;
        }
    }

    /// <summary>The plain name a reference points at (a link's target), or empty.</summary>
    private static string Reference(PropertyData property, UAsset asset) => property switch
    {
        ObjectPropertyData o => ImportName(o.Value, asset),
        SoftObjectPropertyData so => so.Value.AssetPath.AssetName?.ToString() ?? string.Empty,
        _ => string.Empty,
    };

    /// <summary>Resolves a package index to the name it points at (an import or an export).</summary>
    private static string ImportName(FPackageIndex? index, UAsset asset)
    {
        if (index is null) return string.Empty;

        if (index.Index < 0)
        {
            var i = -index.Index - 1;
            if (i >= 0 && i < asset.Imports.Count) return asset.Imports[i].ObjectName?.ToString() ?? string.Empty;
        }
        else if (index.Index < asset.Exports.Count)
        {
            return asset.Exports[index.Index].ObjectName?.ToString() ?? string.Empty;
        }

        return string.Empty;
    }
}
