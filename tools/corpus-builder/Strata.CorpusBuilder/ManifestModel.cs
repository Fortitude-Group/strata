namespace Strata.CorpusBuilder;

/// <summary>One pinned compiler toolchain in the build matrix (contracts/signature-db.md manifest.json).</summary>
public sealed record ToolchainInfo
{
    public required string Compiler { get; init; }

    public required string Version { get; init; }

    public string? ImageDigest { get; init; }
}

/// <summary>
/// The reproducibility manifest written next to `corpus.db` (contracts/signature-db.md). Contains no
/// timestamps or build-host paths (Principle IV / SC-009): every field is either declared build-matrix
/// metadata (from the recipes and the pinned Dockerfiles) or a deterministic function of the signature
/// content (<see cref="BuildReproducibleHash"/>).
/// </summary>
public sealed record CorpusBuildManifest
{
    public required string CorpusVersion { get; init; }

    public required int SchemaVersion { get; init; }

    public required IReadOnlyList<ToolchainInfo> Toolchains { get; init; }

    public required IReadOnlyList<string> OptLevels { get; init; }

    public required IReadOnlyList<string> Arches { get; init; }

    public required int LibraryCount { get; init; }

    /// <summary>ONNX embedding-model version, or "parked" when heuristics-only (SC-004).</summary>
    public string? ModelVersion { get; init; }

    /// <summary>sha256 over the sorted, canonicalised signature content — stable across re-runs (SC-009).</summary>
    public required string BuildReproducibleHash { get; init; }
}
