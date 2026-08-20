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
    /// <summary>Functions below this instruction count are too small to fingerprint reliably (their
    /// MinHash collides coincidentally) and are non-distinctive — excluded from matching to kill the
    /// dominant cross-library false-positive source.</summary>
    public const int MinInstructions = 8;

    private readonly double _similarityThreshold;

    public FunctionEvidenceMatcher(double similarityThreshold = 0.75) =>
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

        // Corpus functions carrying an embedding — brute-forced when the target has one (research.md R7),
        // so the learned signal can surface matches the MinHash-LSH candidates miss (the Checkpoint-B case).
        var embeddedCorpus = new List<int>();
        for (int i = 0; i < corpus.FunctionSignatures.Count; i++)
        {
            if (corpus.FunctionSignatures[i].Embedding is { Count: > 0 })
            {
                embeddedCorpus.Add(i);
            }
        }

        // library -> (matched corpus fns, matched target ids, weighted matches, evidence)
        var perLibrary = new Dictionary<string, LibraryAccumulator>(StringComparer.Ordinal);

        foreach (TargetFunction tf in targetFunctions)
        {
            if (tf.Function.Mnemonics.Count < MinInstructions)
            {
                continue; // too small to fingerprint reliably
            }

            (CorpusFunctionSignature corpusFn, double sim)? best = BestMatch(tf.Signature, index, corpus, embeddedCorpus);
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

    private (CorpusFunctionSignature, double)? BestMatch(
        FunctionSignature target, LshIndex index, ICorpus corpus, IReadOnlyList<int> embeddedCorpus)
    {
        CorpusFunctionSignature? best = null;
        double bestSim = 0.0;

        void Consider(CorpusFunctionSignature candidate)
        {
            double sim = MinHash.Similarity(target.NormInsnMinHash, candidate.NormInsnMinHash);

            // Exact CFG-shape agreement is corroborating evidence on top of instruction similarity.
            if (candidate.CfgShapeHash == target.CfgShapeHash)
            {
                sim = Math.Max(sim, 0.75);
            }

            // Learned-embedding channel: cosine of the two embeddings can surface a match the discrete
            // signals miss across compilers/opt levels (the Checkpoint-B case).
            if (target.Embedding is { Count: > 0 } te && candidate.Embedding is { Count: > 0 } ce
                && te.Count == ce.Count)
            {
                sim = Math.Max(sim, Cosine(te, ce));
            }

            if (sim > bestSim)
            {
                bestSim = sim;
                best = candidate;
            }
        }

        foreach (int candidateIdx in index.Candidates(target))
        {
            Consider(index[candidateIdx]);
        }

        // When the target has an embedding, also weigh the embedded corpus functions directly.
        if (target.Embedding is { Count: > 0 })
        {
            foreach (int idx in embeddedCorpus)
            {
                Consider(corpus.FunctionSignatures[idx]);
            }
        }

        return best is not null && bestSim >= _similarityThreshold ? (best, bestSim) : null;
    }

    private static double Cosine(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Count; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        return na > 0 && nb > 0 ? dot / (Math.Sqrt(na) * Math.Sqrt(nb)) : 0.0;
    }

    private sealed class LibraryAccumulator
    {
        public List<CorpusFunctionSignature> MatchedCorpusFns { get; } = [];

        public List<int> MatchedTargetIds { get; } = [];

        public List<EvidenceRecord> Evidence { get; } = [];

        public double WeightedMatches { get; set; }
    }
}
