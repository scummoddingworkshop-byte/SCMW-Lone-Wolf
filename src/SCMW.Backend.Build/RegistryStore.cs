namespace SCMW.Backend.Build;

/// <summary>
/// The tag store inside a cooked <c>AssetRegistry.bin</c>, walked far enough to locate its parts.
/// The reader resolves tag values from it; the writer appends one numberless name and its pairs and
/// patches the two counts. Everything else is copied across untouched by the writer.
/// </summary>
internal sealed class RegistryStore
{
    /// <summary>Buffer offset where the store begins (its magic).</summary>
    public int Start;

    /// <summary>Buffer offset just past the NumberlessNames array (where a new name is inserted).</summary>
    public int NumberlessNamesEnd;

    /// <summary>Buffer offset just past the shared tag-pair array (where new pairs are appended).</summary>
    public int NumberlessPairsEnd;

    /// <summary>Buffer offset just past the store's end magic.</summary>
    public int End;

    public int NumberlessNamesCount;
    public int NumberlessPairsCount;

    /// <summary>NumberlessNames: each entry is a name-table index.</summary>
    public uint[] NumberlessNames = Array.Empty<uint>();

    /// <summary>The store's Names array: (index, number) FName pairs.</summary>
    public (uint Index, uint Number)[] NameEntries = Array.Empty<(uint, uint)>();

    public uint[] AnsiOffsets = Array.Empty<uint>();
    public byte[] AnsiStrings = Array.Empty<byte>();

    /// <summary>The shared tag pool: (key name-index, packed value).</summary>
    public (uint Key, uint Value)[] Pairs = Array.Empty<(uint, uint)>();

    /// <summary>Walks the store, recording where its two appended arrays live and reading its values.</summary>
    public static RegistryStore Parse(ByteWalker r)
    {
        var store = new RegistryStore { Start = r.Position };

        var magic = r.ReadUInt32();
        var textFirst = magic == 0x12345679u;
        var nums = new int[11];
        for (var i = 0; i < 11; i++) nums[i] = r.ReadInt32();

        if (textFirst)
        {
            r.Skip(4);
            for (var i = 0; i < nums[4]; i++) SkipFString(r);
        }

        var numberlessNames = new uint[nums[0]];
        for (var i = 0; i < nums[0]; i++) numberlessNames[i] = r.ReadUInt32();
        store.NumberlessNamesEnd = r.Position;

        var nameEntries = new (uint, uint)[nums[1]];
        for (var i = 0; i < nums[1]; i++)
        {
            var index = r.ReadUInt32();
            var number = 0u;
            if ((index & 0x80000000u) != 0) { index -= 0x80000000u; number = r.ReadUInt32(); }
            nameEntries[i] = (index, number);
        }

        r.Skip(nums[2] * 12);
        for (var i = 0; i < nums[3]; i++) { SkipFName(r); SkipFName(r); SkipFName(r); }
        if (!textFirst) for (var i = 0; i < nums[4]; i++) SkipFString(r);

        var ansiOffsets = new uint[nums[5]];
        for (var i = 0; i < nums[5]; i++) ansiOffsets[i] = r.ReadUInt32();
        r.Skip(nums[6] * 4);
        var ansiStrings = r.ReadBytes(nums[7]);
        r.Skip(nums[8] * 2);

        var pairs = new (uint, uint)[nums[9]];
        for (var i = 0; i < nums[9]; i++) pairs[i] = (r.ReadUInt32(), r.ReadUInt32());
        store.NumberlessPairsEnd = r.Position;

        for (var i = 0; i < nums[10]; i++) { SkipFName(r); r.Skip(4); }
        r.ReadUInt32();                                  // end magic

        store.End = r.Position;
        store.NumberlessNamesCount = nums[0];
        store.NumberlessPairsCount = nums[9];
        store.NumberlessNames = numberlessNames;
        store.NameEntries = nameEntries;
        store.AnsiOffsets = ansiOffsets;
        store.AnsiStrings = ansiStrings;
        store.Pairs = pairs;
        return store;
    }

    /// <summary>Reads an FName's index (and skips its number when it carries one).</summary>
    public static void SkipFName(ByteWalker r)
    {
        var index = r.ReadUInt32();
        if ((index & 0x80000000u) != 0) r.Skip(4);
    }

    /// <summary>Reads a length-prefixed FString and skips its bytes.</summary>
    public static void SkipFString(ByteWalker r)
    {
        var len = r.ReadInt32();
        if (len > 0) r.Skip(len);
        else if (len < 0) r.Skip(-len * 2);
    }

    /// <summary>Skips one asset record's tag handle and everything after it (bundles, chunks, flags).</summary>
    public static void SkipTail(ByteWalker r)
    {
        r.ReadUInt64();                                  // tag-map handle
        SkipTailAfterHandle(r);
    }

    /// <summary>Skips an asset record's tail when the tag handle has already been read.</summary>
    public static void SkipTailAfterHandle(ByteWalker r)
    {
        var bundles = r.ReadInt32();
        for (var i = 0; i < bundles; i++)
        {
            SkipFName(r);
            var assets = r.ReadInt32();
            for (var j = 0; j < assets; j++) { SkipFName(r); SkipFString(r); }
        }
        var chunks = r.ReadInt32();
        r.Skip(chunks * 4);
        r.Skip(4);                                       // package flags
    }
}
