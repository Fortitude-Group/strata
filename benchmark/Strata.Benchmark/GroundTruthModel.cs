namespace Strata.Benchmark;

/// <summary>
/// One expected component in the held-out ground-truth set (contracts/corpus-builder.md,
/// FR-022/023). The ground-truth JSON maps a binary filename to a list of these.
/// </summary>
public sealed record GroundTruthComponent
{
    public required string Library { get; init; }

    public required string Version { get; init; }
}
