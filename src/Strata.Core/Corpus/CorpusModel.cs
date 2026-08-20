using System.Collections.Generic;

namespace Strata.Core.Corpus;

/// <summary>Metadata about a signature corpus artefact (contracts/signature-db.md).</summary>
public sealed record CorpusManifest
{
    public required string CorpusVersion { get; init; }

    public required int SchemaVersion { get; init; }

    public required int LibraryCount { get; init; }

    /// <summary>ONNX embedding-model version, or null/"parked" when heuristics-only (SC-004).</summary>
    public string? ModelVersion { get; init; }
}

/// <summary>
/// A distinctive string/constant signature for one library, used by the heuristic string/constant
/// signal (research.md R5 signal a). <see cref="Distinctiveness"/> down-weights strings that also
/// appear in other libraries (research.md R9).
/// </summary>
public sealed record CorpusStringSignature
{
    public required string LibraryName { get; init; }

    public required string Value { get; init; }

    /// <summary>0..1; higher means the string is more unique to this library.</summary>
    public required double Distinctiveness { get; init; }

    public string? Purl { get; init; }

    public string? KnownLicense { get; init; }

    /// <summary>Set when this string uniquely identifies a single version (e.g. an embedded version banner).</summary>
    public string? ExactVersion { get; init; }

    /// <summary>Inclusive version-range bounds this string is known to appear across (when not exact).</summary>
    public string? VersionLow { get; init; }

    public string? VersionHigh { get; init; }
}

/// <summary>
/// A per-function signature for one corpus library (research.md R5 signals b/c). Produced by the same
/// fingerprinting code as scan time (Principle I) so target and corpus functions are directly comparable.
/// </summary>
public sealed record CorpusFunctionSignature
{
    public required string LibraryName { get; init; }

    public required string FunctionName { get; init; }

    public required ulong CfgShapeHash { get; init; }

    public required IReadOnlyList<uint> NormInsnMinHash { get; init; }

    /// <summary>0..1; down-weights functions whose signatures also appear in other libraries (R9).</summary>
    public double Distinctiveness { get; init; } = 1.0;

    public string? ExactVersion { get; init; }

    public string? VersionLow { get; init; }

    public string? VersionHigh { get; init; }
}

/// <summary>
/// A signature corpus: the versioned, indexed set of library signatures scan-time matching runs
/// against — string/constant signatures (signal a) and per-function signatures (signals b/c).
/// </summary>
public interface ICorpus
{
    CorpusManifest Manifest { get; }

    /// <summary>All string/constant signatures. Small enough to index in memory for the seed corpus.</summary>
    IReadOnlyList<CorpusStringSignature> StringSignatures { get; }

    /// <summary>All per-function signatures (may be empty for a string-only seed corpus).</summary>
    IReadOnlyList<CorpusFunctionSignature> FunctionSignatures { get; }

    /// <summary>Total number of distinctive string signatures for a library (coverage denominator, R9).</summary>
    int DistinctiveStringCount(string libraryName);

    /// <summary>Total number of function signatures for a library (function-coverage denominator, R9).</summary>
    int FunctionCount(string libraryName);
}
