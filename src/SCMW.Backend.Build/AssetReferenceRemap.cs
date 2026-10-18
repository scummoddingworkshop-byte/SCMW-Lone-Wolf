namespace SCMW.Backend.Build;

/// <summary>
/// One asset a cloned package may still point at, and where the copy of it now lives. A soft
/// reference stores a whole object path ("/Game/…/MiscFood/VegetableOil.VegetableOil_C"); a cloned
/// item's "_ES" companion uses one to name its item (its "Actor" property). If that is copied
/// untouched, the copy still claims the donor — the game then binds the copy's setup to the donor
/// item and the new item gets none (empty Examine, its components never attach). A remap re-points
/// such a path at the copy instead.
/// </summary>
/// <param name="OldPackagePath">The donor's package path in Unreal's <c>/Game/…</c> form.</param>
/// <param name="NewPackagePath">Where the copy's package now lives, same form.</param>
/// <param name="OldName">The donor's object name (e.g. <c>VegetableOil</c>).</param>
/// <param name="NewName">The copy's object name (e.g. <c>OliveOilTEST</c>).</param>
public sealed record AssetReferenceRemap(
    string OldPackagePath, string NewPackagePath, string OldName, string NewName)
{
    /// <summary>The re-pointed path, or <c>null</c> when <paramref name="path"/> does not name this
    /// asset. The object half keeps its shape — plain, its <c>_C</c> class, or that class's
    /// <c>Default__…_C</c> object — so a class reference stays a class reference.</summary>
    public string? RemapPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var dot = path.LastIndexOf('.');
        var package = dot >= 0 ? path[..dot] : path;
        if (!string.Equals(package, OldPackagePath, StringComparison.Ordinal))
            return null;

        return dot < 0 ? NewPackagePath : $"{NewPackagePath}.{Rename(path[(dot + 1)..])}";
    }

    private string Rename(string name) => name switch
    {
        _ when name == OldName => NewName,
        _ when name == OldName + "_C" => NewName + "_C",
        _ when name == "Default__" + OldName + "_C" => "Default__" + NewName + "_C",
        _ => name,
    };
}
