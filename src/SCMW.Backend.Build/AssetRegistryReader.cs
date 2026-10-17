using System.Text;

namespace SCMW.Backend.Build;

/// <summary>One name/value pair in an asset's tag map (e.g. <c>PrimaryAssetName</c> = the asset's name).</summary>
public sealed record RegistryTag(string Key, string Value);

/// <summary>One asset as the registry index describes it: five names plus its decoded tag map.</summary>
public sealed record RegistryAsset(string OldObjectPath, string PackagePath, string AssetClass,
    string PackageName, string AssetName, IReadOnlyList<RegistryTag> Tags);

/// <summary>What a walk of an <c>AssetRegistry.bin</c> found.</summary>
public sealed record AssetRegistryState(int Version, IReadOnlyList<string> Names, int HashMismatches,
    IReadOnlyList<RegistryAsset> Assets);

/// <summary>
/// Reads a cooked <c>AssetRegistry.bin</c>: its name list (checking every stored hash against our
/// CityHash64) and every asset record with its tags resolved. Read-only — used to verify what the
/// writer produced, and to look up the donor's class and tags when creating content.
/// </summary>
public static class AssetRegistryReader
{
    /// <summary>Reads the whole index. Never throws: a malformed file is a plain reason + false.</summary>
    public static bool TryRead(byte[] data, out AssetRegistryState? state, out string? error)
    {
        state = null;
        error = null;
        try
        {
            var r = new ByteWalker(data);

            r.ReadBytes(16);                                 // version GUID
            var version = r.ReadInt32();
            var nameCount = r.ReadInt32();
            r.ReadInt32();                                   // numStringBytes
            r.ReadUInt64();                                  // hash version

            var hashes = new ulong[nameCount];
            for (var i = 0; i < nameCount; i++) hashes[i] = r.ReadUInt64();

            var lengths = new int[nameCount];
            var utf16 = new bool[nameCount];
            for (var i = 0; i < nameCount; i++)
            {
                var b0 = r.ReadByte();
                var b1 = r.ReadByte();
                utf16[i] = (b0 & 0x80) != 0;
                lengths[i] = ((b0 & 0x7F) << 8) + b1;
            }

            var names = new string[nameCount];
            var mismatches = 0;
            for (var i = 0; i < nameCount; i++)
            {
                var bytes = r.ReadBytes(utf16[i] ? lengths[i] * 2 : lengths[i]);
                names[i] = utf16[i] ? Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes);

                var expected = utf16[i]
                    ? CityHash64.Hash(Encoding.Unicode.GetBytes(LowerAscii(names[i])))
                    : CityHash64.HashName(names[i]);
                if (expected != hashes[i]) mismatches++;
            }

            var store = RegistryStore.Parse(r);

            var assetCount = r.ReadInt32();
            var assets = new List<RegistryAsset>(Math.Max(assetCount, 0));
            for (var i = 0; i < assetCount; i++)
            {
                var oldObjectPath = Resolve(r, names);
                var packagePath = Resolve(r, names);
                var assetClass = Resolve(r, names);
                var packageName = Resolve(r, names);
                var assetName = Resolve(r, names);
                var handle = r.ReadUInt64();
                RegistryStore.SkipTailAfterHandle(r);

                assets.Add(new RegistryAsset(oldObjectPath, packagePath, assetClass, packageName,
                    assetName, ResolveTags(handle, store, names)));
            }

            state = new AssetRegistryState(version, names, mismatches, assets);
            return true;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException
                                    or ArgumentException or OverflowException)
        {
            error = "That file is not an asset index we can read.";
            return false;
        }
    }

    /// <summary>Resolves the tag map an asset record points at (its <c>uint64</c> handle).</summary>
    private static IReadOnlyList<RegistryTag> ResolveTags(ulong handle, RegistryStore store, string[] names)
    {
        if (handle == 0) return Array.Empty<RegistryTag>();

        var begin = (int)(handle & 0xFFFFFFFF);
        var count = (int)((handle >> 32) & 0xFFFF);
        if (begin < 0 || begin + count > store.Pairs.Length) return Array.Empty<RegistryTag>();

        var tags = new List<RegistryTag>(count);
        for (var i = 0; i < count; i++)
        {
            var (key, value) = store.Pairs[begin + i];
            tags.Add(new RegistryTag(NameOf(names, key), ResolveValue(value, store, names)));
        }
        return tags;
    }

    private static string ResolveValue(uint id, RegistryStore store, string[] names)
    {
        var type = (int)(id & 7u);
        var index = (int)(id >> 3);
        return type switch
        {
            0 => AnsiString(index, store),
            2 => index >= 0 && index < store.NumberlessNames.Length
                    ? NameOf(names, store.NumberlessNames[index])
                    : $"<numberless-name {index}>",
            3 => index >= 0 && index < store.NameEntries.Length
                    ? NameOf(names, store.NameEntries[index].Index)
                    : $"<name {index}>",
            _ => $"<type{type}:{index}>",
        };
    }

    private static string AnsiString(int index, RegistryStore store)
    {
        if (index < 0 || index >= store.AnsiOffsets.Length) return $"<ansi {index}>";
        var offset = (int)store.AnsiOffsets[index];
        var length = 0;
        while (offset + length < store.AnsiStrings.Length && store.AnsiStrings[offset + length] != 0) length++;
        return Encoding.UTF8.GetString(store.AnsiStrings, offset, length);
    }

    private static string Resolve(ByteWalker r, string[] names)
    {
        var index = r.ReadUInt32();
        if ((index & 0x80000000u) != 0) { index -= 0x80000000u; r.Skip(4); }
        return NameOf(names, index);
    }

    private static string NameOf(string[] names, uint index) =>
        index < names.Length ? names[index] : $"#{index}";

    private static string LowerAscii(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] >= 'A' && chars[i] <= 'Z') chars[i] = (char)(chars[i] + 32);
        return new string(chars);
    }
}
