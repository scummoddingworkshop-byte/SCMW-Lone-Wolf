using System.Text;

namespace SCMW.Backend.Build;

/// <summary>A brand-new asset to add to the game's index: where it lives, its name, and the vanilla
/// asset it is a renamed copy of (the donor, whose class and tags it borrows).</summary>
public sealed record NewRegistryAsset(string PackagePath, string AssetName, string DonorAssetName);

/// <summary>
/// Adds one brand-new asset to a cooked <c>AssetRegistry.bin</c> — the copy of the game's own index
/// that a content-adding mod must ship. It rebuilds only the two regions that change (the name list
/// and the asset array) and copies everything else across untouched, so the result is the game's
/// whole index plus our one entry.
///
/// A new asset must carry its own <c>PrimaryAssetName</c> tag; copying the donor's whole tag block
/// would make the game see two assets with the same primary id, so a fresh tag block is built here.
/// Proven in game by the probe (a second "Stone Axe" recipe appeared); this is its real version.
/// </summary>
public static class AssetRegistryWriter
{
    /// <summary>Adds <paramref name="asset"/> to <paramref name="registry"/>. Never throws.</summary>
    public static bool TryAddAsset(byte[] registry, NewRegistryAsset asset,
        out byte[]? result, out string? error)
    {
        result = null;
        error = null;
        try
        {
            result = Add(registry, asset, out error);
            return result is not null;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException
                                    or ArgumentException or OverflowException)
        {
            error = "That file is not an asset index we can write.";
            result = null;
            return false;
        }
    }

    private static byte[]? Add(byte[] src, NewRegistryAsset asset, out string? error)
    {
        error = null;
        var r = new ByteWalker(src);

        r.ReadBytes(16);
        var version = r.ReadInt32();
        if (version != 8)
        {
            error = $"Registry version {version} is not supported yet.";
            return null;
        }

        // ---- name list: record its four regions so we can rebuild with the new names ----
        var nameCount = r.ReadInt32();
        r.ReadInt32();                                   // numStringBytes
        r.ReadUInt64();                                  // hash version
        var hashesPos = r.Position;
        r.Skip(nameCount * 8);
        var headersPos = r.Position;
        var lengths = new int[nameCount];
        var utf16 = new bool[nameCount];
        for (var i = 0; i < nameCount; i++)
        {
            var b0 = r.ReadByte();
            var b1 = r.ReadByte();
            utf16[i] = (b0 & 0x80) != 0;
            lengths[i] = ((b0 & 0x7F) << 8) + b1;
        }
        var stringsPos = r.Position;
        var names = new string[nameCount];
        for (var i = 0; i < nameCount; i++)
        {
            var bytes = r.ReadBytes(utf16[i] ? lengths[i] * 2 : lengths[i]);
            names[i] = utf16[i] ? Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes);
        }
        var nameBatchEnd = r.Position;

        // ---- tag store: parse it — we borrow the donor's tag pairs and add our own ----
        var store = RegistryStore.Parse(r);

        // ---- asset array: find the donor's record (to borrow its class + tag block) ----
        var assetCount = r.ReadInt32();
        var entriesStart = r.Position;
        string? donorClass = null;
        ulong donorHandle = 0;
        byte[]? donorRest = null;
        for (var i = 0; i < assetCount; i++)
        {
            Resolve(r, names);                           // old object path
            Resolve(r, names);                           // package path
            var cls = Resolve(r, names);
            Resolve(r, names);                           // package name
            var name = Resolve(r, names);
            var tailStart = r.Position;
            RegistryStore.SkipTail(r);
            if (donorRest is null && name == asset.DonorAssetName)
            {
                donorClass = cls;
                donorHandle = BitConverter.ToUInt64(src, tailStart);   // the tag-map handle
                donorRest = src[(tailStart + 8)..r.Position];          // bundles / chunks / flags
            }
        }
        if (donorRest is null || donorClass is null)
        {
            error = $"The donor asset \"{asset.DonorAssetName}\" was not found in the registry.";
            return null;
        }
        var entriesEnd = r.Position;
        var tailSections = src[entriesEnd..];

        // ---- work out the (possibly new) name indices ----
        var all = new List<string>(names);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < names.Length; i++) index[names[i]] = i;
        var added = new List<string>();
        int Ensure(string n)
        {
            if (index.TryGetValue(n, out var existing)) return existing;
            var i = all.Count;
            all.Add(n);
            index[n] = i;
            added.Add(n);
            return i;
        }

        var packageName = asset.PackagePath + "/" + asset.AssetName;
        var oldObjectPath = packageName + "." + asset.AssetName;
        var classIdx = Ensure(donorClass);
        var packagePathIdx = Ensure(asset.PackagePath);
        var assetIdx = Ensure(asset.AssetName);
        var packageNameIdx = Ensure(packageName);
        var oldObjectPathIdx = Ensure(oldObjectPath);
        var primaryNameKeyIdx = (uint)Ensure("PrimaryAssetName");

        // ---- fresh tag block: the donor's pairs, but PrimaryAssetName points at OUR name.
        //      Copying the donor's block would make the new asset claim the donor's primary id. ----
        var donorPairBegin = (int)(donorHandle & 0xFFFFFFFF);
        var donorPairCount = (int)((donorHandle >> 32) & 0xFFFF);
        if ((donorHandle >> 63) == 0)
        {
            error = "The donor asset's tags are not in the numberless form we support.";
            return null;
        }
        if (donorPairBegin + donorPairCount > store.Pairs.Length)
        {
            error = "The donor asset's tag pairs point outside the tag store.";
            return null;
        }

        var newNumberlessNameIndex = store.NumberlessNamesCount;   // where our name will be appended
        var newPairBegin = store.NumberlessPairsCount;             // where our pairs will be appended
        var newPairs = new List<(uint Key, uint Value)>(donorPairCount);
        for (var t = 0; t < donorPairCount; t++)
        {
            var pair = store.Pairs[donorPairBegin + t];
            var value = pair.Key == primaryNameKeyIdx
                ? ((uint)newNumberlessNameIndex << 3) | 2u          // EValueType.NumberlessName
                : pair.Value;
            newPairs.Add((pair.Key, value));
        }
        var newHandle = (1UL << 63) | ((ulong)donorPairCount << 32) | (uint)newPairBegin;

        // ---- the new asset record: the five names, the tag handle, then bundles/chunks/flags ----
        var entry = new List<byte>();
        WriteFName(entry, oldObjectPathIdx);
        WriteFName(entry, packagePathIdx);
        WriteFName(entry, classIdx);
        WriteFName(entry, packageNameIdx);
        WriteFName(entry, assetIdx);
        entry.AddRange(BitConverter.GetBytes(newHandle));
        entry.AddRange(donorRest);

        // ---- rebuild the name list with the added names ----
        var newStrings = new List<byte>();
        var newHeaders = new List<byte>();
        var newHashes = new List<byte>();
        foreach (var n in added)
        {
            var bytes = Encoding.UTF8.GetBytes(n);
            newStrings.AddRange(bytes);
            newHeaders.Add((byte)((bytes.Length >> 8) & 0x7F));
            newHeaders.Add((byte)(bytes.Length & 0xFF));
            newHashes.AddRange(BitConverter.GetBytes(CityHash64.HashName(n)));
        }

        var outBytes = new List<byte>(src.Length + newStrings.Count + 64);
        outBytes.AddRange(src[..hashesPos]);                                   // GUID + version + counts + hash version
        outBytes.AddRange(src[hashesPos..(hashesPos + nameCount * 8)]);        // existing name hashes
        outBytes.AddRange(newHashes);                                          // correct hashes for the new names
        outBytes.AddRange(src[headersPos..(headersPos + nameCount * 2)]);      // existing name headers
        outBytes.AddRange(newHeaders);                                         // new name headers
        outBytes.AddRange(src[stringsPos..nameBatchEnd]);                      // existing name strings
        outBytes.AddRange(newStrings);                                         // new name strings
        outBytes.AddRange(RebuildStore(src, store, (uint)assetIdx, newPairs)); // tag store + our name & pairs
        outBytes.AddRange(BitConverter.GetBytes(assetCount + 1));              // asset count
        outBytes.AddRange(src[entriesStart..entriesEnd]);                      // existing asset records
        outBytes.AddRange(entry);                                              // the new asset record
        outBytes.AddRange(tailSections);                                       // dependency + package sections

        var output = outBytes.ToArray();

        // Patch the name count (offset 20) and the string bytes (offset 24) at the front.
        var originalStringBytes = BitConverter.ToInt32(src, 24);
        BitConverter.GetBytes(nameCount + added.Count).CopyTo(output, 20);
        BitConverter.GetBytes(originalStringBytes + newStrings.Count).CopyTo(output, 24);
        return output;
    }

    /// <summary>Returns the tag store with one extra numberless name and the new pairs appended,
    /// and its two counts patched.</summary>
    private static byte[] RebuildStore(byte[] src, RegistryStore store, uint appendedNameIndex,
                                       List<(uint Key, uint Value)> appendedPairs)
    {
        var bytes = new List<byte>(store.End - store.Start + 4 + appendedPairs.Count * 8);
        bytes.AddRange(src[store.Start..store.NumberlessNamesEnd]);
        bytes.AddRange(BitConverter.GetBytes(appendedNameIndex));                       // new NumberlessName
        bytes.AddRange(src[store.NumberlessNamesEnd..store.NumberlessPairsEnd]);
        foreach (var p in appendedPairs)
        {
            bytes.AddRange(BitConverter.GetBytes(p.Key));
            bytes.AddRange(BitConverter.GetBytes(p.Value));
        }
        bytes.AddRange(src[store.NumberlessPairsEnd..store.End]);

        var output = bytes.ToArray();
        // The eleven counts sit right after the 4-byte magic: nums[i] at offset 4 + i*4.
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            output.AsSpan(4), store.NumberlessNamesCount + 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            output.AsSpan(40), store.NumberlessPairsCount + appendedPairs.Count);
        return output;
    }

    private static void WriteFName(List<byte> to, int index) => to.AddRange(BitConverter.GetBytes((uint)index));

    private static string Resolve(ByteWalker r, string[] names)
    {
        var index = r.ReadUInt32();
        if ((index & 0x80000000u) != 0) { index -= 0x80000000u; r.Skip(4); }
        return index < names.Length ? names[index] : $"#{index}";
    }
}
