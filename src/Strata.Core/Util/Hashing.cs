using System;
using System.Text;

namespace Strata.Core.Util;

/// <summary>Deterministic, allocation-light hashing helpers (Principle IV — stable across runs/machines).</summary>
public static class Hashing
{
    private const ulong FnvOffset = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    public static ulong Fnv1a64(string s) => Fnv1a64(Encoding.UTF8.GetBytes(s));

    public static ulong Fnv1a64(ReadOnlySpan<byte> data)
    {
        ulong hash = FnvOffset;
        foreach (byte b in data)
        {
            hash ^= b;
            hash *= FnvPrime;
        }

        return hash;
    }

    /// <summary>A seeded 32-bit mixing hash used to build MinHash permutations (splitmix-style).</summary>
    public static uint Seeded32(ulong value, uint seed)
    {
        ulong x = value + 0x9E3779B97F4A7C15UL + seed * 0xD1B54A32D192ED03UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return (uint)(x & 0xFFFFFFFF);
    }
}
