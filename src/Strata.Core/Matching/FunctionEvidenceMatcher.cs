using Strata.Core.Corpus;
using Strata.Core.Fingerprinting;
using Strata.Core.Model;

namespace Strata.Core.Matching;

/// <summary>Per-library evidence produced by matching target functions against the corpus.</summary>
public sealed record LibraryFunctionEvidence(
    string LibraryName,
    double Coverage,
    IReadOnlyList<EvidenceRecord> Evidence,
    IReadOnlyList<CorpusFunctionSignature> MatchedCorpusFunctions,
    IReadOnlyList<int> MatchedTargetFunctionIds);

/// <summary>
/// Matches recovered target functions against corpus function signatures via LSH candidate retrieval +
/// MinHash verification (research.md R5/R6). Aggregates into per-library function coverage, weighted by
/// function distinctiveness (R9).
/// </summary>
public sealed class FunctionEvidenceMatcher
{
    private readonly double _similarityThreshold;

    public FunctionEvidenceMatcher(double similarityThreshold = 0.7) =>
        _similarityThreshold = similarityThreshold;

    public IReadOnlyList<LibraryFunctionEvidence> Match(
        IReadOnlyList<TargetFunction> targetFunctions, ICorpus corpus)
    {
        var result = new List<LibraryFunctionEvidence>();
        if (corpus.FunctionSignatures.Count == 0 || targetFunctions.Count == 0)
        {
            return result;
        }

        var index = new LshIndex(corpus.FunctionSignatures);

        // library -> (matched corpus fns, matched target ids, weighted matches, evidence)
        var perLibrary = new Dictionary<string, LibraryAccumulator>(StringComparer.Ordinal);

        foreach (TargetFunction tf in targetFunctions)
        {
            (CorpusFunctionSignature corpusFn, double sim)? best = BestMatch(tf.Signature, index);
            if (best is null)
            {
                continue;
            }

            (CorpusFunctionSignature corpusFn, double sim) = best.Value;
            if (!perLibrary.TryGetValue(corpusFn.LibraryName, out LibraryAccumulator? acc))
            {
                acc = new LibraryAccumulator();
                perLibrary[corpusFn.LibraryName] = acc;
            }

            acc.MatchedCorpusFns.Add(corpusFn);
            acc.MatchedTargetIds.Add(tf.Function.Id);
            acc.WeightedMatches += corpusFn.Distinctiveness;
            acc.Evidence.Add(new EvidenceRecord
            {
                Kind = EvidenceKind.MatchedFunction,
                Signal = SignalKind.NormInsn,
                Detail = $"target fn @0x{tf.Function.StartAddress:x} ≈ {corpusFn.LibraryName}:{corpusFn.FunctionName} (sim {sim:F2})",
                Strength = corpusFn.Distinctiveness * sim,
            });
        }

        foreach ((string library, LibraryAccumulator acc) in perLibrary)
        {
            int libTotal = Math.Max(1, corpus.FunctionCount(library));
            double coverage = Math.Clamp(acc.MatchedCorpusFns.Count / (double)libTotal, 0.0, 1.0);
            result.Add(new LibraryFunctionEvidence(
                library,
                coverage,
                acc.Evidence.OrderByDescending(e => e.Strength).ToList(),
                acc.MatchedCorpusFns,
                acc.MatchedTargetIds));
        }

        return result.OrderByDescending(r => r.Coverage).ThenBy(r => r.LibraryName, StringComparer.Ordinal).ToList();
    }

    private (CorpusFunctionSignature, double)? BestMatch(FunctionSignature target, LshIndex index)
    {
        CorpusFunctionSignature? best = null;
        double bestSim = 0.0;

        foreach (int candidateIdx in index.Candidates(target))
        {
            CorpusFunctionSignature candidate = index[candidateIdx];
            double sim = MinHash.Similarity(target.NormInsnMinHash, candidate.NormInsnMinHash);

            // Exact CFG-shape agreement is corroborating evidence on top of instruction similarity.
            if (candidate.CfgShapeHash == target.CfgShapeHash)
            {
                sim = Math.Max(sim, 0.75);
            }

            if (sim > bestSim)
            {
                bestSim = sim;
                best = candidate;
            }
        }

        return best is not null && bestSim >= _similarityThreshold ? (best, bestSim) : null;
    }

    private sealed class LibraryAccumulator
    {
        public List<CorpusFunctionSignature> MatchedCorpusFns { get; } = [];

        public List<int> MatchedTargetIds { get; } = [];

        public List<EvidenceRecord> Evidence { get; } = [];

        public double WeightedMatches { get; set; }
    }
}
