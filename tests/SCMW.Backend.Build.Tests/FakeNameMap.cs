using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace SCMW.Backend.Build.Tests;

/// <summary>
/// A tiny in-memory name table. UAssetAPI's <c>FName</c> needs a name map, but a whole game
/// package is not; this stub lets tests build a property tree and check the writer's path walk
/// without the game files.
/// </summary>
internal sealed class FakeNameMap : INameMap
{
    private readonly List<FString> _names = new();

    public IReadOnlyList<FString> GetNameMapIndexList() => _names;

    public void ClearNameIndexList() => _names.Clear();

    public void SetNameReference(int index, FString value)
    {
        while (_names.Count <= index) _names.Add(new FString(""));
        _names[index] = value;
    }

    public FString GetNameReference(int index) => _names[index];

    public bool ContainsNameReference(FString search) => _names.Contains(search);

    public int SearchNameReference(FString search) => _names.IndexOf(search);

    public int AddNameReference(FString name, bool forceAddDuplicates, bool skipFixes)
    {
        var index = _names.IndexOf(name);
        if (index >= 0 && !forceAddDuplicates) return index;
        _names.Add(name);
        return _names.Count - 1;
    }

    public bool CanCreateDummies() => true;

    /// <summary>Makes an FName backed by this map, so the property carries a usable name.</summary>
    public FName Name(string value) => FName.FromString(this, value);
}
