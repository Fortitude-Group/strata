using System.Collections.Generic;
using Strata.Core.Model;

namespace Strata.Vuln.Model;

/// <summary>
/// One affected-version range for a library within a <see cref="VulnSnapshotEntry"/>. Bounds follow
/// OSV convention: <see cref="Introduced"/> is inclusive, <see cref="Fixed"/> is exclusive (the fixed
/// version itself is not affected). A null <see cref="Fixed"/> means "not fixed as of this snapshot".
/// </summary>
public sealed record VulnAffectedRange
{
    public required string Introduced { get; init; }

    public string? Fixed { get; init; }
}

/// <summary>One library's affected-version ranges within a <see cref="VulnSnapshotEntry"/>.</summary>
public sealed record VulnAffectedLibrary
{
    public required string Library { get; init; }

    public required IReadOnlyList<VulnAffectedRange> Ranges { get; init; }
}

/// <summary>A single published vulnerability record as carried in the pinned snapshot (research.md R13).</summary>
public sealed record VulnSnapshotEntry
{
    public required string Id { get; init; }

    public required VulnSource Source { get; init; }

    public string? Severity { get; init; }

    public required IReadOnlyList<VulnAffectedLibrary> Affected { get; init; }
}

/// <summary>
/// The pinned, cached vulnerability-data snapshot (research.md R13): OSV-primary, deterministic,
/// offline. <see cref="SnapshotVersion"/> is stamped onto every <see cref="VulnerabilityReference"/>
/// produced from it, per the determinism invariant (data-model.md).
/// </summary>
public sealed record VulnSnapshot
{
    public required string SnapshotVersion { get; init; }

    public required IReadOnlyList<VulnSnapshotEntry> Vulnerabilities { get; init; }
}
