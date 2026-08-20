using System;
using System.Collections.Generic;
using System.Linq;

namespace Strata.Core.Corpus;

/// <summary>
/// A trivial in-memory <see cref="ICorpus"/>. Backs unit tests and the web demo's bundled corpus, and
/// is the shape the SQLite store hydrates into for the seed corpus (small enough to hold in memory).
/// </summary>
public sealed class InMemoryCorpus : ICorpus
{
    private readonly Dictionary<string, int> _distinctiveCounts;
    private readonly Dictionary<string, int> _functionCounts;

    public InMemoryCorpus(
        CorpusManifest manifest,
        IReadOnlyList<CorpusStringSignature> signatures,
        IReadOnlyList<CorpusFunctionSignature>? functionSignatures = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(signatures);
        Manifest = manifest;
        StringSignatures = signatures;
        FunctionSignatures = functionSignatures ?? [];
        _distinctiveCounts = signatures
            .GroupBy(s => s.LibraryName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        _functionCounts = FunctionSignatures
            .GroupBy(s => s.LibraryName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
    }

    public CorpusManifest Manifest { get; }

    public IReadOnlyList<CorpusStringSignature> StringSignatures { get; }

    public IReadOnlyList<CorpusFunctionSignature> FunctionSignatures { get; }

    public int DistinctiveStringCount(string libraryName) =>
        _distinctiveCounts.TryGetValue(libraryName, out int count) ? count : 0;

    public int FunctionCount(string libraryName) =>
        _functionCounts.TryGetValue(libraryName, out int count) ? count : 0;
}
