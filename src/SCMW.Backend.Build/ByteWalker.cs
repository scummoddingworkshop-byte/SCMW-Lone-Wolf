using System.Buffers.Binary;

namespace SCMW.Backend.Build;

/// <summary>A tiny forward-only reader over an in-memory buffer, for the registry reader/writer.</summary>
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

    public byte[] ReadBytes(int count)
    {
        var b = new byte[count];
        Array.Copy(_data, Position, b, 0, count);
        Position += count;
        return b;
    }

    public void Skip(int count) => Position += count;
}
