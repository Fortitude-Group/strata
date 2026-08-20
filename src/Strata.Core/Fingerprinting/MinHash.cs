using Strata.Core.Util;

namespace Strata.Core.Fingerprinting;

/// <summary>
/// Fixed-permutation MinHash over a set of 64-bit shingles. Two functions' MinHash signatures estimate
/// the Jaccard similarity of their shingle sets (research.md R5/R6), which is what the LSH index and the
/// function matcher use. Deterministic: same input ⇒ same signature (Principle IV).
/// </summary>
public static class MinHash
{
    public const int Permutations = 32;

    public static IReadOnlyList<uint> Compute(IEnumerable<ulong> shingles)
    {
        var mins = new uint[Permutations];
        for (int p = 0; p < Permutations; p++)
        {
            mins[p] = uint.MaxValue;
        }

        bool any = false;
        foreach (ulong shingle in shingles)
        {
            any = true;
            for (uint p = 0; p < Permutations; p++)
            {
                uint h = Hashing.Seeded32(shingle, p + 1);
                if (h < mins[p])
                {
                    mins[p] = h;
                }
            }
        }

        return any ? mins : [];
    }

    /// <summary>Estimated Jaccard similarity from two equal-length MinHash signatures (0 if incomparable).</summary>
    public static double Similarity(IReadOnlyList<uint> a, IReadOnlyList<uint> b)
    {
        if (a.Count == 0 || a.Count != b.Count)
        {
            return 0.0;
        }

        int equal = 0;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] == b[i])
            {
                equal++;
            }
        }

        return (double)equal / a.Count;
    }
}
