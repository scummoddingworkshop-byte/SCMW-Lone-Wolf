using System.Buffers.Binary;
using System.Text;

namespace SCMW.GameModel;

/// <summary>
/// THROWAWAY PROBE (2026-10-17). Walks a cooked Unreal <c>AssetRegistry.bin</c> — the game's index
/// of every asset — and reports its structure, resolving real entries so we can see exactly what an
/// asset record holds. Its purpose is to prove our map of the file's layout and to learn what the
/// app's future "add new content" registry writer must produce. It writes nothing.
/// See docs/research/game-structure/mods-for-research.md (the "our own probe" section).
/// </summary>
internal static class RegistryProbe
{
    internal static int Run(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"File not found: {path}");
            return 3;
        }

        var data = File.ReadAllBytes(path);
        var r = new ByteWalker(data);
        Console.WriteLine($"file: {path}  ({data.Length:N0} bytes)");

        try
        {
            // ---- header: version GUID (16 bytes) + version int32 ----
            r.ReadBytes(16);
            var version = r.ReadInt32();
            Console.WriteLine($"version = {version}");

            // ---- name batch: keep the names, so entries can be resolved ----
            var nameCount = r.ReadInt32();
            Console.WriteLine($"name count = {nameCount:N0}");
            var names = new string[nameCount];
            if (nameCount > 0)
            {
                r.Skip(4);                 // numStringBytes
                r.Skip(8);                 // hash version
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
                for (var i = 0; i < nameCount; i++)
                {
                    var bytes = r.ReadBytes(utf16[i] ? lengths[i] * 2 : lengths[i]);
                    names[i] = utf16[i] ? Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes);
                }
                for (var i = 0; i < Math.Min(8, nameCount); i++)
                    Console.WriteLine($"  name[{i}] = \"{names[i]}\"  hash = 0x{hashes[i]:X16}");

                var ok = 0; long bad = 0; var badShown = 0;
                for (var i = 0; i < nameCount; i++)
                {
                    var bytes = utf16[i]
                        ? Encoding.Unicode.GetBytes(LowerAscii(names[i]))
                        : Encoding.UTF8.GetBytes(LowerAscii(names[i]));
                    var h = CityHash64.Hash(bytes);
                    if (h == hashes[i]) { ok++; }
                    else { bad++; if (badShown++ < 5) Console.WriteLine($"  hash MISMATCH \"{names[i]}\" computed=0x{h:X16} stored=0x{hashes[i]:X16}"); }
                }
                Console.WriteLine($"hash check: {ok:N0} match, {bad:N0} mismatch");
            }

            // ---- the tag store (FStore) — skipped wholesale ----
            var storeStart = r.Position;
            var magic = r.ReadUInt32();
            var textFirst = magic == 0x12345679u;
            var nums = new int[11];
            for (var i = 0; i < 11; i++) nums[i] = r.ReadInt32();
            Console.WriteLine($"tag store @ 0x{storeStart:X} magic=0x{magic:X} textFirst={textFirst}");
            for (var i = 0; i < 11; i++) Console.WriteLine($"    nums[{i}] = {nums[i]:N0}");
            if (textFirst) { r.Skip(4); for (var i = 0; i < nums[4]; i++) SkipFString(r); }

            var numberlessNames = new uint[nums[0]];
            for (var i = 0; i < nums[0]; i++) numberlessNames[i] = r.ReadUInt32();

            var nameEntries = new (uint Index, uint Number)[nums[1]];
            for (var i = 0; i < nums[1]; i++)
            {
                var ix = r.ReadUInt32(); var nm = 0u;
                if ((ix & 0x80000000u) != 0) { ix -= 0x80000000u; nm = r.ReadUInt32(); }
                nameEntries[i] = (ix, nm);
            }

            r.Skip(nums[2] * 12);
            for (var i = 0; i < nums[3]; i++) { SkipFName(r); SkipFName(r); SkipFName(r); }
            if (!textFirst) for (var i = 0; i < nums[4]; i++) SkipFString(r);

            var ansiOffsets = new uint[nums[5]];
            for (var i = 0; i < nums[5]; i++) ansiOffsets[i] = r.ReadUInt32();
            r.Skip(nums[6] * 4);
            var ansiStrings = r.ReadBytes(nums[7]);
            r.Skip(nums[8] * 2);

            var tagArrayPos = r.Position;      // nums[9] x 8 bytes: the shared tag pool
            r.Skip(nums[9] * 8);
            for (var i = 0; i < nums[10]; i++) { SkipFName(r); r.Skip(4); }
            r.ReadUInt32(); // end magic

            string NameOf(uint v) => v < names.Length ? names[v] : $"#{v}";
            string ResolveValue(uint id)
            {
                var type = (int)(id & 7u);
                var index = (int)id >> 3;
                switch (type)
                {
                    case 0: // AnsiString
                        if (index >= 0 && index < ansiOffsets.Length)
                        {
                            var off = (int)ansiOffsets[index]; var len = 0;
                            while (off + len < ansiStrings.Length && ansiStrings[off + len] != 0) len++;
                            return Encoding.UTF8.GetString(ansiStrings, off, len);
                        }
                        return $"<ansi {index}>";
                    case 2: // NumberlessName -> NameMap index
                        return index >= 0 && index < numberlessNames.Length ? NameOf(numberlessNames[index]) : $"<nn {index}>";
                    case 3: // Name -> FName in the store's Names array
                        return index >= 0 && index < nameEntries.Length ? NameOf(nameEntries[index].Index) : $"<name {index}>";
                    default:
                        return $"<type{type}:{index}>";
                }
            }

            // ---- asset data: show the first few, and the target recipe ----
            var assetCount = r.ReadInt32();
            Console.WriteLine($"asset count = {assetCount:N0}");
            Console.WriteLine();
            var shown = 0;
            for (var i = 0; i < assetCount; i++)
            {
                var entry = ReadAsset(r, names);
                var secmw = entry.AssetName.Contains("SECMW", StringComparison.OrdinalIgnoreCase);
                var wanted = entry.AssetName.Contains("Stone_Axe", StringComparison.OrdinalIgnoreCase)
                             || entry.AssetName.Contains("Sappers", StringComparison.OrdinalIgnoreCase);
                if (i < 2 || secmw || (wanted && shown < 6))
                {
                    Console.WriteLine($"asset[{i}]");
                    Console.WriteLine($"    oldObjectPath = \"{entry.OldObjectPath}\"");
                    Console.WriteLine($"    PackagePath   = \"{entry.PackagePath}\"");
                    Console.WriteLine($"    AssetClass    = \"{entry.AssetClass}\"");
                    Console.WriteLine($"    PackageName   = \"{entry.PackageName}\"");
                    Console.WriteLine($"    AssetName     = \"{entry.AssetName}\"");
                    Console.WriteLine($"    tagMapSize    = 0x{entry.TagMapSize:X16}");
                    Console.WriteLine($"    chunkIds      = [{string.Join(", ", entry.ChunkIds)}]");
                    Console.WriteLine($"    bundles/chunks/flags bytes = {entry.TailBytes}");
                    if (entry.TagMapSize != 0)
                    {
                        var savePos = r.Position;
                        var hasNumberlessKeys = (entry.TagMapSize >> 63) > 0;
                        var idx = (int)(entry.TagMapSize & 0xFFFFFFFF);
                        var cnt = (int)((entry.TagMapSize >> 32) & 0xFFFF);
                        Console.WriteLine($"    tags: numberlessKeys={hasNumberlessKeys} begin={idx:N0} count={cnt}");
                        for (var t = 0; t < cnt && t < 12; t++)
                        {
                            r.Position = tagArrayPos + (idx + t) * 8;
                            var a = r.ReadUInt32();
                            var b = r.ReadUInt32();
                            Console.WriteLine($"       [{t}] \"{NameOf(a)}\" = \"{ResolveValue(b)}\"");
                        }
                        r.Position = savePos;
                    }
                    Console.WriteLine();
                    if (wanted) shown++;
                }
            }

            Console.WriteLine($"dependency/package sections follow; file used {r.Position:N0} / {data.Length:N0} bytes.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED at byte {r.Position:N0}: {ex.Message}");
            return 1;
        }
    }

    private readonly record struct AssetEntry(
        string OldObjectPath, string PackagePath, string AssetClass, string PackageName, string AssetName,
        ulong TagMapSize, List<int> ChunkIds, long TailBytes);

    private static AssetEntry ReadAsset(ByteWalker r, string[] names)
    {
        var oldObjectPath = Resolve(r, names);   // old object path (before RemoveAssetPathFNames)
        var packagePath = Resolve(r, names);
        var assetClass = Resolve(r, names);
        var packageName = Resolve(r, names);
        var assetName = Resolve(r, names);

        var tailStart = r.Position;
        var mapSize = r.ReadUInt64();
        var bundles = r.ReadInt32();
        for (var i = 0; i < bundles; i++)
        {
            SkipFName(r);
            var assets = r.ReadInt32();
            for (var j = 0; j < assets; j++) { SkipFName(r); SkipFString(r); }
        }
        var chunks = r.ReadInt32();
        var chunkIds = new List<int>();
        for (var i = 0; i < chunks; i++) chunkIds.Add(r.ReadInt32());
        r.Skip(4); // package flags
        var tail = r.Position - tailStart;

        return new AssetEntry(oldObjectPath, packagePath, assetClass, packageName, assetName, mapSize, chunkIds, tail);
    }

    private static string Resolve(ByteWalker r, string[] names)
    {
        var index = r.ReadUInt32();
        if ((index & 0x80000000u) != 0) { index -= 0x80000000u; r.Skip(4); }
        return index < names.Length ? names[index] : $"#{index}";
    }

    private static void SkipFName(ByteWalker r)
    {
        var index = r.ReadUInt32();
        if ((index & 0x80000000u) != 0) r.Skip(4);
    }

    private static void SkipFString(ByteWalker r)
    {
        var len = r.ReadInt32();
        if (len > 0) r.Skip(len);
        else if (len < 0) r.Skip(-len * 2);
    }

    /// <summary>Lower-cases A–Z only, matching the engine's case-insensitive name hash.</summary>
    private static string LowerAscii(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] >= 'A' && chars[i] <= 'Z') chars[i] = (char)(chars[i] + 32);
        return new string(chars);
    }
}

/// <summary>A tiny forward-only reader over an in-memory buffer, for the registry probe.</summary>
internal sealed class ByteWalker
{
    private readonly byte[] _data;

    public ByteWalker(byte[] data) => _data = data;

    public int Position { get; set; }

    public byte ReadByte() => _data[Position++];

    public uint ReadUInt32()
    {
        var v = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(Position));
        Position += 4;
        return v;
    }

    public int ReadInt32()
    {
        var v = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(Position));
        Position += 4;
        return v;
    }

    public ulong ReadUInt64()
    {
        var v = BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(Position));
        Position += 8;
        return v;
    }

    public long ReadInt64()
    {
        var v = BinaryPrimitives.ReadInt64LittleEndian(_data.AsSpan(Position));
        Position += 8;
        return v;
    }

    public byte[] ReadBytes(int count)
    {
        var b = new byte[count];
        Array.Copy(_data, Position, b, 0, count);
        Position += count;
        return b;
    }

    public void Skip(int count) => Position += count;
}
