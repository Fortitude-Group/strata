namespace Strata.Core.Model;

/// <summary>Progress event streamed during a scan for UX (FR-024). Never alters the result.</summary>
public sealed record ScanProgress(string Stage, string Message, double Fraction);

/// <summary>
/// The top-level output of a scan. A pure function of (target bytes, corpus version, tool version,
/// vuln snapshot) — the determinism invariant (Principle IV). Nondeterministic presentation fields
/// (serial number, timestamp) live in the SBOM output layer, not here.
/// </summary>
public sealed record ScanResult
{
    public required ScanTarget Target { get; init; }

    public IReadOnlyList<IdentifiedComponent> Components { get; init; } = [];

    public IReadOnlyList<UnidentifiedRegion> UnidentifiedRegions { get; init; } = [];

    public required string CorpusVersion { get; init; }

    public required string ToolVersion { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
