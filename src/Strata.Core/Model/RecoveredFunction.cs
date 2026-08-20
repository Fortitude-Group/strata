namespace Strata.Core.Model;

/// <summary>A basic block within a recovered function's control-flow graph.</summary>
public sealed record BasicBlock(int Index, ulong StartAddress, ulong EndAddress);

/// <summary>
/// A function boundary recovered from a (possibly stripped) target (FR-006). Carries a recovery
/// confidence so downstream stages can weight or discard uncertain recoveries (research.md R4).
/// </summary>
public sealed record RecoveredFunction
{
    public required int Id { get; init; }

    public required ulong StartAddress { get; init; }

    public required ulong EndAddress { get; init; }

    public IReadOnlyList<BasicBlock> BasicBlocks { get; init; } = [];

    /// <summary>CFG edges as (fromBlockIndex, toBlockIndex) pairs.</summary>
    public IReadOnlyList<(int From, int To)> Edges { get; init; } = [];

    /// <summary>Normalised opcode mnemonics in body order (operands abstracted) — the substrate for the
    /// instruction-sequence signal (research.md R5 signal c).</summary>
    public IReadOnlyList<string> Mnemonics { get; init; } = [];

    /// <summary>0..1 confidence that this is a real function boundary.</summary>
    public double RecoveryConfidence { get; init; } = 1.0;

    public ulong SizeBytes => EndAddress > StartAddress ? EndAddress - StartAddress : 0;
}
