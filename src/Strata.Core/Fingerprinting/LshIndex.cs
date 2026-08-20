using System.Collections.Generic;
using Strata.Core.Corpus;
using Strata.Core.Model;
using Strata.Core.Util;

namespace Strata.Core.Fingerprinting;

/// <summary>
/// Banded MinHash-LSH over corpus function signatures (research.md R6). Splits each 32-permutation
/// signature into bands; functions that share a band land in the same bucket, giving sub-linear
/// candidate retrieval. Also indexes exact CFG-shape hashes as a second recall channel.
/// </summary>
public sealed class LshIndex
{
    private const int Bands = 8;
    private const int Rows = MinHash.Permutations / Bands;

    private readonly Dictionary<ulong, List<int>> _bandBuckets = [];
    private readonly Dictionary<ulong, List<int>> _byCfgShape = [];
    private readonly IReadOnlyList<CorpusFunctionSignature> _functions;

    public LshIndex(IReadOnlyList<CorpusFunctionSignature> functions)
    {
        _functions = functions;
        for (int i = 0; i < functions.Count; i++)
        {
            CorpusFunctionSignature f = functions[i];
            AddTo(_byCfgShape, f.CfgShapeHash, i);

            if (f.NormInsnMinHash.Count == MinHash.Permutations)
            {
                for (int band = 0; band < Bands; band++)
                {
                    AddTo(_bandBuckets, BandKey(band, f.NormInsnMinHash), i);
                }
            }
        }
    }

    /// <summary>Corpus-function indices that plausibly match the target (union of band + CFG buckets).</summary>
    public IReadOnlyCollection<int> Candidates(FunctionSignature target)
    {
        var candidates = new HashSet<int>();

        if (_byCfgShape.TryGetValue(target.CfgShapeHash, out List<int>? cfgHits))
        {
            foreach (int i in cfgHits)
            {
                candidates.Add(i);
            }
        }

        if (target.NormInsnMinHash.Count == MinHash.Permutations)
        {
            for (int band = 0; band < Bands; band++)
            {
                if (_bandBuckets.TryGetValue(BandKey(band, target.NormInsnMinHash), out List<int>? hits))
                {
                    foreach (int i in hits)
                    {
                        candidates.Add(i);
                    }
                }
            }
        }

        return candidates;
    }

    public CorpusFunctionSignature this[int index] => _functions[index];

    private static ulong BandKey(int band, IReadOnlyList<uint> minhash)
    {
        // Mix the band index and its rows into one key.
        ulong h = Hashing.Seeded32((ulong)band, 0x51ED);
        for (int r = 0; r < Rows; r++)
        {
            h = (h * 1099511628211UL) ^ minhash[(band * Rows) + r];
        }

        return h;
    }

    private static void AddTo(Dictionary<ulong, List<int>> map, ulong key, int index)
    {
        if (!map.TryGetValue(key, out List<int>? list))
        {
            list = [];
            map[key] = list;
        }

        list.Add(index);
    }
}
