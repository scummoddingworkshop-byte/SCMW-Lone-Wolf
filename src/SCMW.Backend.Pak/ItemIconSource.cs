// src/SCMW.Backend.Pak/ItemIconSource.cs
// The seam between ItemIconExtractor and CUE4Parse. The extractor only needs two
// things from the game: "which texture does this item use?" and "give me that
// texture as a bitmap". Both live behind IItemIconSource, so unit tests can drive
// the extractor with a tiny fake and never touch a real pak.
//
// Resolution follows the verified Plan 2b Task 1 findings
// (docs/superpowers/notes/plan2b-icon-spike-findings.md):
//   1. the item's own CDO's GridInventoryIcon property;
//   2. else the <AssetName>_ES sibling's GridInventoryIcon (the majority case);
//   3. else GeneralPurposeIcon / HandsAndShouldersIcon / any property whose name
//      contains "icon" (covers the _inventoryIcon_* family).
// The reference is a SoftObjectProperty holding a "/Game/.../ICO_X.ICO_X" path;
// CUE4Parse's FixPath resolves that to the mounted "SCUM/Content/" form.
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
using SkiaSharp;

namespace SCMW.Backend.Pak;

/// <summary>How an asset id relates to an icon, before any decoding is attempted.</summary>
public enum IconSourceOutcome
{
    /// <summary>An icon texture reference was found; <see cref="IconSourceResult.TexturePath"/> is set.</summary>
    Resolved,

    /// <summary>The item loaded fine but has no icon anywhere in its chain (letter-tile case).</summary>
    NoIcon,

    /// <summary>The item package/CDO could not be loaded — not an item, or a transient read failure.</summary>
    NotLoaded,
}

public readonly record struct IconSourceResult(IconSourceOutcome Outcome, string? TexturePath)
{
    public static IconSourceResult Resolved(string texturePath) => new(IconSourceOutcome.Resolved, texturePath);
    public static IconSourceResult NoIcon => new(IconSourceOutcome.NoIcon, null);
    public static IconSourceResult NotLoaded => new(IconSourceOutcome.NotLoaded, null);
}

/// <summary>
/// The extractor's view of the game files. Implemented for real by
/// <see cref="PakIconSource"/> over an <see cref="IPakMount"/>, and by a fake in
/// unit tests.
/// </summary>
public interface IItemIconSource
{
    /// <summary>
    /// Resolves the icon texture for an asset id (package path, with or without
    /// ".uasset"). Must not throw for a missing/unreadable item: return
    /// <see cref="IconSourceOutcome.NotLoaded"/> instead.
    /// </summary>
    IconSourceResult Resolve(string assetId);

    /// <summary>
    /// Decodes the texture at the given soft/package path into a bitmap, or null
    /// when it cannot be decoded. The caller owns (disposes) a returned bitmap.
    /// </summary>
    SKBitmap? Decode(string texturePath);
}

/// <summary>Reads item icon references (and decodes textures) straight from the mounted pak.</summary>
public sealed class PakIconSource : IItemIconSource
{
    // Checked in priority order on each CDO before the broader "contains icon" sweep.
    private static readonly string[] PreferredIconProperties =
    {
        "GridInventoryIcon",
        "GeneralPurposeIcon",
        "HandsAndShouldersIcon",
    };

    private const string EsSuffix = "_ES";

    private readonly IPakMount _mount;

    // Built lazily (only when an item has no icon of its own) so the common
    // own-CDO hit never pays for enumerating the mounted file list.
    private readonly Lazy<HashSet<string>> _mountedPaths;

    // "What a recipe places" -> the icon it shows, built lazily the first time a base element
    // (which has no icon of its own) asks. Only recipes carry an icon for a base element.
    private readonly Lazy<Dictionary<string, string>> _recipeIcons;

    public PakIconSource(IPakMount mount)
    {
        _mount = mount ?? throw new ArgumentNullException(nameof(mount));
        _mountedPaths = new Lazy<HashSet<string>>(
            () => new HashSet<string>(mount.FilePaths, StringComparer.OrdinalIgnoreCase));
        _recipeIcons = new Lazy<Dictionary<string, string>>(BuildRecipeIcons);
    }

    public IconSourceResult Resolve(string assetId) => Resolve(assetId, depth: 0);

    private IconSourceResult Resolve(string assetId, int depth)
    {
        if (string.IsNullOrWhiteSpace(assetId))
            return IconSourceResult.NotLoaded;

        // Most items are blueprints: read the class default object.
        if (TryLoadCdo(assetId, out var cdo) && cdo is not null)
        {
            if (TryReadIconReference(cdo, out var ownPath))
                return IconSourceResult.Resolved(ownPath!);

            // Task 1: most icon-less items carry the property only on their _ES sibling.
            // A sibling that exists but cannot be read is a transient failure, not proof
            // of "no icon", so it must stay retryable (see ResolveSiblingOutcome).
            var siblingExists = SiblingExists(assetId);
            var siblingLoaded = false;
            var siblingHasIcon = false;
            string? siblingPath = null;

            if (siblingExists &&
                TryLoadCdo(assetId + EsSuffix, out var sibling) && sibling is not null)
            {
                siblingLoaded = true;
                siblingHasIcon = TryReadIconReference(sibling, out siblingPath);
            }

            var outcome = ResolveSiblingOutcome(siblingExists, siblingLoaded, siblingHasIcon, siblingPath);
            if (outcome.Outcome == IconSourceOutcome.Resolved)
                return outcome;

            // A base element has no icon of its own: the recipe that places it (its "Product")
            // carries the icon the build menu shows, so borrow that.
            if (TryRecipeIcon(assetId, out var recipeIcon))
                return IconSourceResult.Resolved(recipeIcon!);

            return outcome;
        }

        // Not a blueprint (a data asset, e.g. a recipe): read the asset object itself.
        if (TryLoadAssetObject(assetId, out var asset) && asset is not null)
        {
            if (TryReadIconReference(asset, out var assetIcon))
                return IconSourceResult.Resolved(assetIcon!);

            // A recipe with no icon of its own: use the item it makes (its "Product"). One level only.
            if (depth == 0 && TryReadProductPath(asset, out var productPath))
            {
                var fromProduct = Resolve(productPath!, depth + 1);
                if (fromProduct.Outcome == IconSourceOutcome.Resolved)
                    return fromProduct;
            }

            return IconSourceResult.NoIcon;
        }

        // Could not load it at all: not an item, or a transient failure. Retryable — never
        // recorded as "no icon".
        return IconSourceResult.NotLoaded;
    }

    /// <summary>
    /// Loads the asset object itself (<c>Package.ObjectName</c>) — how a data asset such as a recipe
    /// is addressed; it has no class default object. Mirrors <see cref="TryLoadCdo"/>.
    /// </summary>
    private bool TryLoadAssetObject(string assetId, out UObject? obj)
    {
        obj = null;
        var path = StripUasset(assetId);
        var name = path[(path.LastIndexOf('/') + 1)..];
        if (name.Length == 0)
            return false;

        if (!_mount.TryLoadPackageObject($"{path}.{name}", out var loaded) || loaded is null)
            return false;

        obj = loaded;
        return true;
    }

    /// <summary>The icon a recipe shows for the asset it places, or false when none places it.</summary>
    private bool TryRecipeIcon(string assetId, out string? icon)
        => _recipeIcons.Value.TryGetValue(StripUasset(assetId), out icon);

    /// <summary>
    /// Builds the "what a recipe places" map once: each recipe's <c>Product</c> package path (the base
    /// element it builds) to the icon the build menu shows for it. Only recipes carry an icon for a
    /// base element, so a base element borrows its placing recipe's icon.
    /// </summary>
    private Dictionary<string, string> BuildRecipeIcons()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _mount.FilePaths)
        {
            if (!path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) continue;
            if (!path.Contains("/Recipes/", StringComparison.OrdinalIgnoreCase)) continue;

            var file = path[(path.LastIndexOf('/') + 1)..];
            if (!file.StartsWith("CR_", StringComparison.OrdinalIgnoreCase)) continue;

            var id = path[..^7];
            if (!TryLoadAssetObject(id, out var recipe) || recipe is null) continue;

            string? product = null, icon = null;
            foreach (var prop in recipe.Properties)
            {
                var name = string.IsNullOrEmpty(prop.Name.Text) ? prop.Name.ToString() : prop.Name.Text;
                if (name == "Product") product = prop.Tag?.GenericValue?.ToString();
                else if (name == "Icon") icon = prop.Tag?.GenericValue?.ToString();
            }

            var key = NormalizeProductPath(product);
            if (key is not null && !string.IsNullOrWhiteSpace(icon) && !map.ContainsKey(key))
                map[key] = icon;
        }
        return map;
    }

    /// <summary>A data asset's <c>Product</c> — the item it makes — as a package path, or false.</summary>
    private static bool TryReadProductPath(UObject asset, out string? productPath)
    {
        productPath = null;
        foreach (var prop in asset.Properties)
        {
            var name = string.IsNullOrEmpty(prop.Name.Text) ? prop.Name.ToString() : prop.Name.Text;
            if (name != "Product") continue;
            productPath = NormalizeProductPath(prop.Tag?.GenericValue?.ToString());
            return productPath is not null;
        }
        return false;
    }

    /// <summary>
    /// A recipe's <c>Product</c> value ("/Game/.../BP_Base_X.BP_Base_X_C") as the base element's
    /// package path ("SCUM/Content/.../BP_Base_X"). Null when there is no path.
    /// </summary>
    internal static string? NormalizeProductPath(string? productValue)
    {
        if (string.IsNullOrWhiteSpace(productValue))
            return null;

        var path = productValue;
        var dot = path.IndexOf('.');
        if (dot >= 0) path = path[..dot];

        return path.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase)
            ? "SCUM/Content/" + path["/Game/".Length..]
            : path;
    }

    private static string StripUasset(string assetId) =>
        assetId.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) ? assetId[..^7] : assetId;

    /// <summary>
    /// The final outcome once the item's own CDO loaded but carried no icon. A
    /// sibling that exists but could not be loaded is retryable (<c>NotLoaded</c>);
    /// a sibling that is genuinely absent, or loaded with no icon, is a permanent
    /// <c>NoIcon</c>.
    /// </summary>
    public static IconSourceResult ResolveSiblingOutcome(
        bool siblingExists, bool siblingLoaded, bool siblingHasIcon, string? siblingIconPath)
    {
        if (siblingHasIcon)
            return IconSourceResult.Resolved(siblingIconPath!);
        return siblingExists && !siblingLoaded
            ? IconSourceResult.NotLoaded
            : IconSourceResult.NoIcon;
    }

    /// <summary>
    /// Whether the item's <c>&lt;AssetName&gt;_ES.uasset</c> sibling is present in the
    /// mounted file list. Distinguishes "genuinely absent" (permanent NoIcon) from
    /// "present but failed to load" (retryable).
    /// </summary>
    public bool SiblingExists(string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId))
            return false;

        var path = assetId.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
            ? assetId[..^7]
            : assetId;
        return _mountedPaths.Value.Contains(path + EsSuffix + ".uasset");
    }

    public SKBitmap? Decode(string texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
            return null;

        try
        {
            if (!_mount.TryLoadPackageObject(texturePath, out var obj) || obj is not UTexture2D texture)
                return null;

            return TextureDecoder.Decode(texture, ETexturePlatform.DesktopMobile);
        }
        catch (Exception)
        {
            // A single bad texture must degrade to "failed", never abort a run.
            return null;
        }
    }

    /// <summary>
    /// Loads the class default object (Default__&lt;AssetName&gt;_C) for a package
    /// path. Mirrors PakContentSource's CDO convention.
    /// </summary>
    private bool TryLoadCdo(string assetId, out UObject? cdo)
    {
        cdo = null;
        var path = assetId.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
            ? assetId[..^7]
            : assetId;

        var name = path[(path.LastIndexOf('/') + 1)..];
        if (name.Length == 0)
            return false;

        if (!_mount.TryLoadPackageObject($"{path}.Default__{name}_C", out var loaded) || loaded is null)
            return false;

        cdo = loaded;
        return true;
    }

    private static bool TryReadIconReference(UObject cdo, out string? texturePath)
    {
        texturePath = null;

        // Pass 1: the preferred names, in priority order. (The property is a
        // SoftObjectProperty; its value string is a "/Game/.../ICO_X.ICO_X" path.)
        foreach (var wanted in PreferredIconProperties)
        {
            foreach (var prop in cdo.Properties)
            {
                var name = string.IsNullOrEmpty(prop.Name.Text) ? prop.Name.ToString() : prop.Name.Text;
                if (!string.Equals(name, wanted, StringComparison.Ordinal))
                    continue;

                var value = prop.Tag?.GenericValue?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    texturePath = value;
                    return true;
                }
            }
        }

        // Pass 2: any property whose name contains "icon" — covers the
        // _inventoryIcon_* / *_inventoryIconInHands_* / *_outsideInventoryIcon_* family.
        foreach (var prop in cdo.Properties)
        {
            var name = string.IsNullOrEmpty(prop.Name.Text) ? prop.Name.ToString() : prop.Name.Text;
            if (!name.Contains("icon", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = prop.Tag?.GenericValue?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                texturePath = value;
                return true;
            }
        }

        return false;
    }
}
