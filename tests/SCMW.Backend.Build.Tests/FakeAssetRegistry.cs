using System.Text;
using SCMW.Backend.Build;

namespace SCMW.Backend.Build.Tests;

/// <summary>
/// A tiny, hand-built Unreal <c>AssetRegistry.bin</c> (version 8) for tests: a name list, a
/// text-first tag store, and a few asset records — the smallest shape the reader and writer both
/// understand. Real-registry tests guard this shape against the game itself.
/// </summary>
internal sealed class FakeAssetRegistry
{
    public int Version { get; set; } = 8;

    public ulong HashVersion { get; set; } = 0xC1640000UL;

    /// <summary>The store's NumberlessNames array: name-table indices.</summary>
    public List<uint> NumberlessNames { get; } = new();

    /// <summary>The store's shared tag pairs (<c>uint32 key</c> + <c>uint32 packed value</c>).</summary>
    public List<(uint Key, uint Value)> Pairs { get; } = new();

    private readonly List<string> _names = new();
    private readonly List<FakeRecord> _records = new();

    /// <summary>The index of a name, adding it to the name list if it is new.</summary>
    public int Name(string name)
    {
        var i = _names.IndexOf(name);
        if (i >= 0) return i;
        _names.Add(name);
        return _names.Count - 1;
    }

    /// <summary>Registers a record and its names (so the name list is complete before it is written).</summary>
    public void AddRecord(FakeRecord record)
    {
        foreach (var name in record.Fields) Name(name);
        _records.Add(record);
    }

    public byte[] Build()
    {
        var encoded = _names.Select(Encoding.UTF8.GetBytes).ToArray();

        var bytes = new List<byte>();
        bytes.AddRange(new byte[16]);                                   // version GUID
        bytes.AddRange(BitConverter.GetBytes(Version));
        bytes.AddRange(BitConverter.GetBytes(_names.Count));
        bytes.AddRange(BitConverter.GetBytes(encoded.Sum(b => b.Length))); // numStringBytes
        bytes.AddRange(BitConverter.GetBytes(HashVersion));

        foreach (var name in _names)
            bytes.AddRange(BitConverter.GetBytes(CityHash64.HashName(name)));
        foreach (var text in encoded)
        {
            bytes.Add((byte)((text.Length >> 8) & 0x7F));               // utf8 flag clear (bit 0x80)
            bytes.Add((byte)(text.Length & 0xFF));
        }
        foreach (var text in encoded)
            bytes.AddRange(text);

        bytes.AddRange(BuildStore());

        bytes.AddRange(BitConverter.GetBytes(_records.Count));
        foreach (var record in _records)
            bytes.AddRange(BuildRecord(record));

        return bytes.ToArray();
    }

    private byte[] BuildStore()
    {
        var nums = new int[11];
        nums[0] = NumberlessNames.Count;
        nums[9] = Pairs.Count;

        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(0x12345679u));             // text-first magic
        foreach (var n in nums) bytes.AddRange(BitConverter.GetBytes(n));
        bytes.AddRange(BitConverter.GetBytes(0u));                      // the uint32 after the counts
        foreach (var n in NumberlessNames) bytes.AddRange(BitConverter.GetBytes(n));
        foreach (var (key, value) in Pairs)
        {
            bytes.AddRange(BitConverter.GetBytes(key));
            bytes.AddRange(BitConverter.GetBytes(value));
        }
        bytes.AddRange(BitConverter.GetBytes(0u));                      // end magic
        return bytes.ToArray();
    }

    private byte[] BuildRecord(FakeRecord record)
    {
        var bytes = new List<byte>();
        foreach (var field in record.Fields)
            bytes.AddRange(BitConverter.GetBytes((uint)_names.IndexOf(field)));
        bytes.AddRange(BitConverter.GetBytes(record.TagHandle));
        bytes.AddRange(BitConverter.GetBytes(0));                       // bundle count
        bytes.AddRange(BitConverter.GetBytes(record.ChunkIds.Length));
        foreach (var chunk in record.ChunkIds) bytes.AddRange(BitConverter.GetBytes(chunk));
        bytes.AddRange(BitConverter.GetBytes(0u));                      // package flags
        return bytes.ToArray();
    }
}

/// <summary>One asset record: its five names, its tag handle, and its chunk ids.</summary>
internal sealed record FakeRecord(string[] Fields, ulong TagHandle, int[] ChunkIds)
{
    public string AssetName => Fields[4];
}
