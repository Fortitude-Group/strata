namespace Strata.Core.Model;

/// <summary>
/// The concrete basis for a component or version claim (FR-014/027, Principle XII "Explain Every
/// Number"). No component and no version claim may exist without at least one of these.
/// </summary>
public sealed record EvidenceRecord
{
    public required EvidenceKind Kind { get; init; }

    public required SignalKind Signal { get; init; }

    /// <summary>Human-readable detail, e.g. the matched string value or function↔corpus-function link.</summary>
    public required string Detail { get; init; }

    /// <summary>0..1 contribution of this evidence to the component's confidence.</summary>
    public double Strength { get; init; } = 1.0;
}
