using System.Buffers.Binary;

namespace SCMW.GameModel;

/// <summary>
/// Google's CityHash64, ported for the registry probe. UE4.27 hashes each name in the asset
/// registry's name list with <c>CityHash64</c> of the lower-cased name (see
/// <c>FNameHash::GenerateHash</c> in <c>UnrealNames.cpp</c>). We only need it to stamp correct
/// look-up values on the names we add.
/// </summary>
internal static class CityHash64
{
    private const ulong K0 = 0xc3a5c85c97cb3127UL;
    private const ulong K1 = 0xb492b66fbe98f273UL;
    private const ulong K2 = 0x9ae16a3b2f90404fUL;

    private static ulong Fetch64(ReadOnlySpan<byte> s, int i) => BinaryPrimitives.ReadUInt64LittleEndian(s[i..]);
    private static uint Fetch32(ReadOnlySpan<byte> s, int i) => BinaryPrimitives.ReadUInt32LittleEndian(s[i..]);
    private static ulong Rotate(ulong v, int shift) => shift == 0 ? v : (v >> shift) | (v << (64 - shift));
    private static ulong ShiftMix(ulong v) => v ^ (v >> 47);
    private static ulong Bswap64(ulong v) => BinaryPrimitives.ReverseEndianness(v);

    private static ulong Hash128to64(ulong lo, ulong hi)
    {
        const ulong kMul = 0x9ddfea08eb382d69UL;
        var a = (lo ^ hi) * kMul;
        a ^= a >> 47;
        var b = (hi ^ a) * kMul;
        b ^= b >> 47;
        b *= kMul;
        return b;
    }

    private static ulong HashLen16(ulong u, ulong v) => Hash128to64(u, v);

    private static ulong HashLen16(ulong u, ulong v, ulong mul)
    {
        var a = (u ^ v) * mul;
        a ^= a >> 47;
        var b = (v ^ a) * mul;
        b ^= b >> 47;
        b *= mul;
        return b;
    }

    private static ulong HashLen0to16(ReadOnlySpan<byte> s, int len)
    {
        if (len >= 8)
        {
            var mul = K2 + (ulong)len * 2;
            var a = Fetch64(s, 0) + K2;
            var b = Fetch64(s, len - 8);
            var c = Rotate(b, 37) * mul + a;
            var d = (Rotate(a, 25) + b) * mul;
            return HashLen16(c, d, mul);
        }
        if (len >= 4)
        {
            var mul = K2 + (ulong)len * 2;
            var a = Fetch32(s, 0);
            return HashLen16((ulong)len + ((ulong)a << 3), Fetch32(s, len - 4), mul);
        }
        if (len > 0)
        {
            byte a = s[0], b = s[len >> 1], c = s[len - 1];
            var y = (uint)(a + (b << 8));
            var z = (uint)(len + (c << 2));
            return ShiftMix(y * K2 ^ z * K0) * K2;
        }
        return K2;
    }

    private static ulong HashLen17to32(ReadOnlySpan<byte> s, int len)
    {
        var mul = K2 + (ulong)len * 2;
        var a = Fetch64(s, 0) * K1;
        var b = Fetch64(s, 8);
        var c = Fetch64(s, len - 8) * mul;
        var d = Fetch64(s, len - 16) * K2;
        return HashLen16(Rotate(a + b, 43) + Rotate(c, 30) + d, a + Rotate(b + K2, 18) + c, mul);
    }

    private static (ulong A, ulong B) WeakHashLen32WithSeeds(
        ulong w, ulong x, ulong y, ulong z, ulong a, ulong b)
    {
        a += w;
        b = Rotate(b + a + z, 21);
        var c = a;
        a += x;
        a += y;
        b += Rotate(a, 44);
        return (a + z, b + c);
    }

    private static (ulong A, ulong B) WeakHashLen32WithSeeds(ReadOnlySpan<byte> s, int i, ulong a, ulong b)
        => WeakHashLen32WithSeeds(Fetch64(s, i), Fetch64(s, i + 8), Fetch64(s, i + 16), Fetch64(s, i + 24), a, b);

    private static ulong HashLen33to64(ReadOnlySpan<byte> s, int len)
    {
        var mul = K2 + (ulong)len * 2;
        var a = Fetch64(s, 0) * K2;
        var b = Fetch64(s, 8);
        var c = Fetch64(s, len - 24);
        var d = Fetch64(s, len - 32);
        var e = Fetch64(s, 16) * K2;
        var f = Fetch64(s, 24) * 9;
        var g = Fetch64(s, len - 8);
        var h = Fetch64(s, len - 16) * mul;
        var u = Rotate(a + g, 43) + (Rotate(b, 30) + c) * 9;
        var v = ((a + g) ^ d) + f + 1;
        var w = Bswap64((u + v) * mul) + h;
        var x = Rotate(e + f, 42) + c;
        var y = (Bswap64((v + w) * mul) + g) * mul;
        var z = e + f + c;
        a = Bswap64((x + z) * mul + y) + b;
        b = ShiftMix((z + a) * mul + d + h) * mul;
        return b + x;
    }

    /// <summary>The registry's stored look-up value for a name: CityHash64 of its lower-cased bytes.</summary>
    public static ulong HashName(string name) => Hash(System.Text.Encoding.UTF8.GetBytes(LowerAscii(name)));

    private static string LowerAscii(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] >= 'A' && chars[i] <= 'Z') chars[i] = (char)(chars[i] + 32);
        return new string(chars);
    }

    public static ulong Hash(ReadOnlySpan<byte> s)
    {
        var len = s.Length;
        if (len <= 32) return len <= 16 ? HashLen0to16(s, len) : HashLen17to32(s, len);
        if (len <= 64) return HashLen33to64(s, len);

        var x = Fetch64(s, len - 40);
        var y = Fetch64(s, len - 16) + Fetch64(s, len - 56);
        var z = HashLen16(Fetch64(s, len - 48) + (ulong)len, Fetch64(s, len - 24));
        var v = WeakHashLen32WithSeeds(s, len - 64, (ulong)len, z);
        var w = WeakHashLen32WithSeeds(s, len - 32, y + K1, x);
        x = x * K1 + Fetch64(s, 0);

        var n = (len - 1) & ~63;
        var off = 0;
        do
        {
            x = Rotate(x + y + v.A + Fetch64(s, off + 8), 37) * K1;
            y = Rotate(y + v.B + Fetch64(s, off + 48), 42) * K1;
            x ^= w.B;
            y += v.A + Fetch64(s, off + 40);
            z = Rotate(z + w.A, 33) * K1;
            v = WeakHashLen32WithSeeds(s, off, v.B * K1, x + w.A);
            w = WeakHashLen32WithSeeds(s, off + 32, z + w.B, y + Fetch64(s, off + 16));
            (z, x) = (x, z);
            off += 64;
            n -= 64;
        } while (n != 0);

        return HashLen16(HashLen16(v.A, w.A) + ShiftMix(y) * K1 + z, HashLen16(v.B, w.B) + x);
    }
}
