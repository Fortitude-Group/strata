namespace Strata.Core.Model;

/// <summary>
/// The multi-signal fingerprint of one recovered function (FR-007). Each signal targets a different
/// invariance so they fail independently (research.md R5). All signals are pure functions of the
/// function bytes + architecture (Principle IV).
/// </summary>
public sealed record FunctionSignature
{
    public required int FunctionId { get; init; }

    /// <summary>Signal (a): normalised references to strings and constants.</summary>
    public IReadOnlyCollection<string> StringConstRefs { get; init; } = [];

    /// <summary>Signal (b): CFG-shape hash (block count, edge structure, loop nesting).</summary>
    public ulong CfgShapeHash { get; init; }

    /// <summary>Signal (c): MinHash of normalised opcode n-grams.</summary>
    public IReadOnlyList<uint> NormInsnMinHash { get; init; } = [];

    /// <summary>Signal (d): optional learned embedding; null when the model is parked (SC-004).</summary>
    public IReadOnlyList<float>? Embedding { get; init; }
}
