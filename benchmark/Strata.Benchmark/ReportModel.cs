namespace Strata.Benchmark;

/// <summary>Per-library precision/recall breakdown (contracts/corpus-builder.md).</summary>
public sealed record LibraryMetrics
{
    public required string Library { get; init; }

    public required int TruePositives { get; init; }

    public required int FalsePositives { get; init; }

    public required int FalseNegatives { get; init; }

    public required double Precision { get; init; }

    public required double Recall { get; init; }
}

/// <summary>Outcome for one held-out binary.</summary>
public sealed record BinaryResult
{
    public required string FileName { get; init; }

    public required IReadOnlyList<string> ExpectedLibraries { get; init; }

    public required IReadOnlyList<string> PredictedLibraries { get; init; }

    public required double ElapsedMilliseconds { get; init; }

    public string? Error { get; init; }
}

/// <summary>Checkpoint A/B pass/fail (contracts/corpus-builder.md SC-001/002/003).</summary>
public sealed record CheckpointResult
{
    public required string Checkpoint { get; init; }

    public required double PrecisionThreshold { get; init; }

    public required double RecallThreshold { get; init; }

    public double? VersionAccuracyThreshold { get; init; }

    public required bool Pass { get; init; }
}

/// <summary>
/// The published benchmark report (contracts/corpus-builder.md, SC-010: published regardless of
/// pass/fail — never hide a failed checkpoint).
/// </summary>
public sealed record BenchmarkReport
{
    public required string Checkpoint { get; init; }

    public required string CorpusVersion { get; init; }

    public required int BinaryCount { get; init; }

    public required double AggregatePrecision { get; init; }

    public required double AggregateRecall { get; init; }

    /// <summary>Fraction of matched (expected ∩ predicted) libraries whose minor version was correct.</summary>
    public required double VersionResolutionAccuracy { get; init; }

    public required double TotalWallTimeMilliseconds { get; init; }

    public required CheckpointResult CheckpointVerdict { get; init; }

    public required IReadOnlyList<LibraryMetrics> PerLibrary { get; init; }

    public required IReadOnlyList<BinaryResult> PerBinary { get; init; }
}
