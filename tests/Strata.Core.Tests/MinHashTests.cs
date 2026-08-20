using Strata.Core.Fingerprinting;

namespace Strata.Core.Tests;

public sealed class MinHashTests
{
    [Fact]
    public void Identical_shingle_sets_have_similarity_one()
    {
        ulong[] shingles = [1, 2, 3, 4, 5];

        IReadOnlyList<uint> a = MinHash.Compute(shingles);
        IReadOnlyList<uint> b = MinHash.Compute(shingles);

        Assert.Equal(1.0, MinHash.Similarity(a, b));
    }

    [Fact]
    public void Disjoint_shingle_sets_have_low_similarity()
    {
        ulong[] setA = Enumerable.Range(0, 200).Select(i => (ulong)i).ToArray();
        ulong[] setB = Enumerable.Range(10_000, 200).Select(i => (ulong)i).ToArray();

        IReadOnlyList<uint> a = MinHash.Compute(setA);
        IReadOnlyList<uint> b = MinHash.Compute(setB);

        double similarity = MinHash.Similarity(a, b);
        Assert.True(similarity < 0.5, $"expected low similarity for disjoint sets, got {similarity}");
    }

    [Fact]
    public void Compute_returns_permutation_count_values_for_non_empty_input()
    {
        IReadOnlyList<uint> sig = MinHash.Compute([42UL, 7UL]);

        Assert.Equal(MinHash.Permutations, sig.Count);
    }

    [Fact]
    public void Compute_returns_empty_for_no_shingles()
    {
        IReadOnlyList<uint> sig = MinHash.Compute([]);

        Assert.Empty(sig);
    }

    [Fact]
    public void Similarity_of_empty_signatures_is_zero()
    {
        Assert.Equal(0.0, MinHash.Similarity([], []));
    }

    [Fact]
    public void Similarity_of_mismatched_lengths_is_zero()
    {
        IReadOnlyList<uint> a = MinHash.Compute([1UL]);
        uint[] b = [1, 2, 3];

        Assert.Equal(0.0, MinHash.Similarity(a, b));
    }
}
