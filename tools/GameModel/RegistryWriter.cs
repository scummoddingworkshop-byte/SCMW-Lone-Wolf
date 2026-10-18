using System.Buffers.Binary;
using System.Text;

namespace SCMW.GameModel;

/// <summary>
/// THROWAWAY PROTOTYPE (2026-10-17). Adds one asset entry to a cooked Unreal <c>AssetRegistry.bin</c>
/// by rebuilding only the two regions that change — the name list (to hold the new asset's names) and
/// the asset array (to hold its one record) — and copying everything else across untouched. This is
/// the prototype for the app's real "create new content" registry writer. New names are written with
/// zero hashes: the engine reads names by index, so the hashes are only a lookup aid.
/// </summary>
internal static class RegistryWriter
{
    /// <summary>Returns a copy of <paramref name="src"/> with one new asset added, or null with a reason.</summary>
    internal static byte[]? TryAddAsset(
        byte[] src, string packagePath, string assetName, string donorAssetName, out string? error)
    {
        error = null;
        var r = new ByteWalker(src);

        r.ReadBytes(16);
        var version = r.ReadInt32();
        if (version != 8) { error = $"Registry version {version} is not supported yet."; return null; }

        // ---- name list: record its four regions so we can rebuild with the new names ----
        var nameCount = r.ReadInt32();
        r.ReadInt32();                 // numStringBytes
        var hashVersion = r.ReadUInt64();
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
        var store = ParseStore(r);

        // ---- asset array: find the donor's record (to borrow its class + tag block) ----
        var assetCount = r.ReadInt32();
        var entriesStart = r.Position;
        string? donorClass = null;
        ulong donorHandle = 0;
        byte[]? donorRest = null;
        for (var i = 0; i < assetCount; i++)
        {
            Resolve(r, names);              // old object path
            Resolve(r, names);              // package path
            var cls = Resolve(r, names);
            Resolve(r, names);              // package name
            var asset = Resolve(r, names);
            var tailStart = r.Position;
            SkipTail(r);
            if (donorRest is null && asset == donorAssetName)
            {
                donorClass = cls;
                donorHandle = BitConverter.ToUInt64(src, tailStart);   // the tag-map handle
                donorRest = src[(tailStart + 8)..r.Position];          // bundles / chunks / flags
            }
        }
        if (donorRest is null || donorClass is null)
        {
            error = $"The donor asset \"{donorAssetName}\" was not found in the registry.";
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

        var packageName = packagePath + "/" + assetName;
        var oldObjectPath = packageName + "." + assetName;
        var classIdx = Ensure(donorClass);
        var packagePathIdx = Ensure(packagePath);
        var assetIdx = Ensure(assetName);
        var packageNameIdx = Ensure(packageName);
        var oldObjectPathIdx = Ensure(oldObjectPath);
        var primaryNameKeyIdx = (uint)Ensure("PrimaryAssetName");

        // ---- fresh tag block: the donor's pairs, but PrimaryAssetName points at OUR name.
        //      Copying the donor's block would make the new asset claim the donor's primary id. ----
        var donorPairBegin = (int)(donorHandle & 0xFFFFFFFF);
        var donorPairCount = (int)((donorHandle >> 32) & 0xFFFF);
        if ((donorHandle >> 63) == 0)
        {
            error = "The donor asset's tags are not in the numberless form this prototype supports.";
            return null;
        }
        if (donorPairBegin + donorPairCount > store.NumberlessPairs.Length)
        {
            error = "The donor asset's tag pairs point outside the tag store.";
            return null;
        }

        var newNumberlessNameIndex = store.NumberlessNamesCount;   // where our name will be appended
        var newPairBegin = store.NumberlessPairsCount;             // where our pairs will be appended
        var newPairs = new List<(uint Key, uint Value)>(donorPairCount);
        for (var t = 0; t < donorPairCount; t++)
        {
            var pair = store.NumberlessPairs[donorPairBegin + t];
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

        // ---- rebuild the name list with the added names (zero hashes) ----
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
        outBytes.AddRange(src[..hashesPos]);                                  // GUID + version + name count + numStringBytes + hashVersion
        outBytes.AddRange(src[hashesPos..(hashesPos + nameCount * 8)]);        // existing name hashes
        outBytes.AddRange(newHashes);                                          // correct hashes for the new names
        outBytes.AddRange(src[headersPos..(headersPos + nameCount * 2)]);      // existing name headers
        outBytes.AddRange(newHeaders);                                        // new name headers
        outBytes.AddRange(src[stringsPos..nameBatchEnd]);                     // existing name strings
        outBytes.AddRange(newStrings);                                        // new name strings
        outBytes.AddRange(RebuildStore(src, store, (uint)assetIdx, newPairs)); // tag store + our name & pairs
        outBytes.AddRange(BitConverter.GetBytes(assetCount + 1));              // asset count
        outBytes.AddRange(src[entriesStart..entriesEnd]);                     // existing asset records
        outBytes.AddRange(entry);                                             // the new asset record
        outBytes.AddRange(tailSections);                                      // dependency + package sections

        // Patch the name count and numStringBytes at the front of the rebuilt file.
        // (16-byte version GUID + 4-byte version, then the count and the byte size.)
        var result = outBytes.ToArray();
        const int countOffset = 20;
        const int stringBytesOffset = 24;
        var originalStringBytes = BitConverter.ToInt32(src, stringBytesOffset);
        BitConverter.GetBytes(nameCount + added.Count).CopyTo(result, countOffset);
        BitConverter.GetBytes(originalStringBytes + newStrings.Count).CopyTo(result, stringBytesOffset);
        return result;
    }

    /// <summary>Returns a copy of <paramref name="src"/> with one asset's record removed — used to
    /// test whether the game actually reads the index we ship.</summary>
    internal static byte[]? TryRemoveAsset(byte[] src, string assetName, out string? error)
    {
        error = null;
        var r = new ByteWalker(src);

        r.ReadBytes(16);
        var version = r.ReadInt32();
        if (version != 8) { error = $"Registry version {version} is not supported yet."; return null; }

        var nameCount = r.ReadInt32();
        r.ReadInt32();
        r.ReadUInt64();
        r.Skip(nameCount * 8);
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
        for (var i = 0; i < nameCount; i++)
        {
            var bytes = r.ReadBytes(utf16[i] ? lengths[i] * 2 : lengths[i]);
            names[i] = utf16[i] ? Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes);
        }

        SkipStore(r);

        var countPos = r.Position;
        var assetCount = r.ReadInt32();
        var ranges = new List<(int Start, int End, string Name)>(assetCount);
        for (var i = 0; i < assetCount; i++)
        {
            var start = r.Position;
            Resolve(r, names);
            Resolve(r, names);
            Resolve(r, names);
            Resolve(r, names);
            var name = Resolve(r, names);
            SkipTail(r);
            ranges.Add((start, r.Position, name));
        }
        var entriesEnd = r.Position;

        var removed = 0;
        var kept = new List<byte>();
        foreach (var e in ranges)
        {
            if (string.Equals(e.Name, assetName, StringComparison.Ordinal)) { removed++; continue; }
            kept.AddRange(src[e.Start..e.End]);
        }
        if (removed == 0) { error = $"\"{assetName}\" was not found in the registry."; return null; }

        var outBytes = new List<byte>(src.Length);
        outBytes.AddRange(src[..countPos]);
        outBytes.AddRange(BitConverter.GetBytes(assetCount - removed));
        outBytes.AddRange(kept);
        outBytes.AddRange(src[entriesEnd..]);
        return outBytes.ToArray();
    }

    private static void WriteFName(List<byte> to, int index) => to.AddRange(BitConverter.GetBytes((uint)index));

    private static string Resolve(ByteWalker r, string[] names)
    {
        var index = r.ReadUInt32();
        if ((index & 0x80000000u) != 0) { index -= 0x80000000u; r.Skip(4); }
        return index < names.Length ? names[index] : $"#{index}";
    }

    private static void SkipTail(ByteWalker r)
    {
        r.ReadUInt64();               // tag map handle
        var bundles = r.ReadInt32();
        for (var i = 0; i < bundles; i++)
        {
            SkipFName(r);             // bundle name
            var assets = r.ReadInt32();
            for (var j = 0; j < assets; j++)
            {
                SkipFName(r);         // asset path
                SkipFString(r);
            }
        }
        var chunks = r.ReadInt32();
        r.Skip(chunks * 4);
        r.Skip(4);                    // package flags
    }

    private static void SkipFName(ByteWalker r)
    {
        var index = r.ReadUInt32();
        if ((index & 0x80000000u) != 0) r.Skip(4); // numbered name
    }

    private sealed record StoreLayout(
        int Start, int NumberlessNamesEnd, int NumberlessPairsEnd, int End,
        int NumberlessNamesCount, int NumberlessPairsCount,
        (uint Key, uint Value)[] NumberlessPairs);

    /// <summary>Walks the tag store far enough to record where the two arrays we append to live,
    /// and reads the numberless pair table so we can borrow a donor's tags.</summary>
    private static StoreLayout ParseStore(ByteWalker r)
    {
        var start = r.Position;
        var magic = r.ReadUInt32();
        var textFirst = magic == 0x12345679u;
        var nums = new int[11];
        for (var i = 0; i < 11; i++) nums[i] = r.ReadInt32();

        if (textFirst) { r.Skip(4); for (var i = 0; i < nums[4]; i++) SkipFString(r); }

        r.Skip(nums[0] * 4);                       // NumberlessNames — our new name goes on the end
        var numberlessNamesEnd = r.Position;

        for (var i = 0; i < nums[1]; i++) SkipFName(r);
        r.Skip(nums[2] * 12);
        for (var i = 0; i < nums[3]; i++) { SkipFName(r); SkipFName(r); SkipFName(r); }
        if (!textFirst) for (var i = 0; i < nums[4]; i++) SkipFString(r);
        r.Skip(nums[5] * 4);
        r.Skip(nums[6] * 4);
        r.Skip(nums[7]);
        r.Skip(nums[8] * 2);

        var pairs = new (uint Key, uint Value)[nums[9]];
        for (var i = 0; i < nums[9]; i++) pairs[i] = (r.ReadUInt32(), r.ReadUInt32());
        var numberlessPairsEnd = r.Position;

        for (var i = 0; i < nums[10]; i++) { SkipFName(r); r.Skip(4); }
        r.ReadUInt32();                            // end magic
        return new StoreLayout(start, numberlessNamesEnd, numberlessPairsEnd, r.Position,
                               nums[0], nums[9], pairs);
    }

    /// <summary>Returns the tag store with one extra numberless name and the new pairs appended,
    /// and its two counts patched.</summary>
    private static byte[] RebuildStore(byte[] src, StoreLayout store, uint appendedNameIndex,
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

        var result = bytes.ToArray();
        // The eleven counts sit right after the 4-byte magic: nums[i] at offset 4 + i*4.
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), store.NumberlessNamesCount + 1);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(40), store.NumberlessPairsCount + appendedPairs.Count);
        return result;
    }

    private static void SkipStore(ByteWalker r)
    {
        var magic = r.ReadUInt32();
        var textFirst = magic == 0x12345679u;
        var nums = new int[11];
        for (var i = 0; i < 11; i++) nums[i] = r.ReadInt32();
        if (textFirst) { r.Skip(4); for (var i = 0; i < nums[4]; i++) SkipFString(r); }
        r.Skip(nums[0] * 4);
        for (var i = 0; i < nums[1]; i++) SkipFName(r);
        r.Skip(nums[2] * 12);
        for (var i = 0; i < nums[3]; i++) { SkipFName(r); SkipFName(r); SkipFName(r); }
        if (!textFirst) for (var i = 0; i < nums[4]; i++) SkipFString(r);
        r.Skip(nums[5] * 4);
        r.Skip(nums[6] * 4);
        r.Skip(nums[7]);
        r.Skip(nums[8] * 2);
        r.Skip(nums[9] * 8);
        for (var i = 0; i < nums[10]; i++) { SkipFName(r); r.Skip(4); }
        r.ReadUInt32(); // end magic
    }

    private static void SkipFString(ByteWalker r)
    {
        var len = r.ReadInt32();
        if (len > 0) r.Skip(len);
        else if (len < 0) r.Skip(-len * 2);
    }
}
