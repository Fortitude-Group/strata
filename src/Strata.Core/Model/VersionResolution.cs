using System;
using System.Collections.Generic;

namespace Strata.Core.Model;

/// <summary>
/// A resolved library version — exact where the evidence is unambiguous, otherwise a bounded range
/// (FR-013). Enforces the precision invariant: a range is only valid with both bounds, and an exact
/// version with a value. Never report a version more precisely than <see cref="Basis"/> supports.
/// </summary>
public sealed record VersionResolution
{
    public required VersionKind Kind { get; init; }

    /// <summary>Set iff <see cref="Kind"/> is <see cref="VersionKind.Exact"/>.</summary>
    public string? Exact { get; init; }

    /// <summary>Inclusive lower bound iff <see cref="Kind"/> is <see cref="VersionKind.Range"/>.</summary>
    public string? RangeLow { get; init; }

    /// <summary>Inclusive upper bound iff <see cref="Kind"/> is <see cref="VersionKind.Range"/>.</summary>
    public string? RangeHigh { get; init; }

    /// <summary>Version-distinguishing evidence backing this claim (research.md R8).</summary>
    public IReadOnlyList<EvidenceRecord> Basis { get; init; } = [];

    public static VersionResolution OfExact(string version, IReadOnlyList<EvidenceRecord> basis)
    {
        ArgumentException.ThrowIfNullOrEmpty(version);
        return new VersionResolution { Kind = VersionKind.Exact, Exact = version, Basis = basis };
    }

    public static VersionResolution OfRange(string low, string high, IReadOnlyList<EvidenceRecord> basis)
    {
        ArgumentException.ThrowIfNullOrEmpty(low);
        ArgumentException.ThrowIfNullOrEmpty(high);
        return new VersionResolution { Kind = VersionKind.Range, RangeLow = low, RangeHigh = high, Basis = basis };
    }

    /// <summary>Display form: "1.2.11" or "1.2.8–1.2.11".</summary>
    public string Display => Kind == VersionKind.Exact ? Exact! : $"{RangeLow}–{RangeHigh}";
}
