using Strata.Core.Fingerprinting;
using Strata.Core.Model;

namespace Strata.Core.Tests;

public sealed class SignalTests
{
    [Fact]
    public void CfgShapeSignal_is_stable_for_the_same_function_shape()
    {
        RecoveredFunction fn1 = TwoBlockFunction();
        RecoveredFunction fn2 = TwoBlockFunction();

        Assert.Equal(CfgShapeSignal.Compute(fn1), CfgShapeSignal.Compute(fn2));
    }

    [Fact]
    public void CfgShapeSignal_differs_for_structurally_different_functions()
    {
        RecoveredFunction twoBlock = TwoBlockFunction();
        RecoveredFunction threeBlock = ThreeBlockFunction();

        Assert.NotEqual(CfgShapeSignal.Compute(twoBlock), CfgShapeSignal.Compute(threeBlock));
    }

    [Fact]
    public void NormInsnSignal_is_stable_for_the_same_mnemonic_sequence()
    {
        string[] mnemonics = ["push", "mov", "add", "cmp", "jz", "ret"];

        IReadOnlyList<uint> a = NormInsnSignal.Compute(mnemonics);
        IReadOnlyList<uint> b = NormInsnSignal.Compute((string[])mnemonics.Clone());

        Assert.Equal(1.0, MinHash.Similarity(a, b));
    }

    [Fact]
    public void NormInsnSignal_differs_for_different_instruction_sequences()
    {
        IReadOnlyList<uint> a = NormInsnSignal.Compute(["push", "mov", "add", "cmp", "jz", "ret"]);
        IReadOnlyList<uint> b = NormInsnSignal.Compute(["xor", "call", "pop", "test", "je", "leave"]);

        Assert.True(MinHash.Similarity(a, b) < 1.0);
    }

    [Fact]
    public void NormInsnSignal_of_empty_mnemonics_is_empty()
    {
        Assert.Empty(NormInsnSignal.Compute([]));
    }

    [Fact]
    public void NormInsnSignal_pads_sequences_shorter_than_the_ngram_size()
    {
        // Fewer mnemonics than NGram (4) still produces a non-empty signature.
        IReadOnlyList<uint> sig = NormInsnSignal.Compute(["push", "ret"]);
        Assert.NotEmpty(sig);
    }

    private static RecoveredFunction TwoBlockFunction() => new()
    {
        Id = 1,
        StartAddress = 0,
        EndAddress = 8,
        BasicBlocks = [new BasicBlock(0, 0, 4), new BasicBlock(1, 4, 8)],
        Edges = [(0, 1)],
    };

    private static RecoveredFunction ThreeBlockFunction() => new()
    {
        Id = 2,
        StartAddress = 0,
        EndAddress = 12,
        BasicBlocks = [new BasicBlock(0, 0, 4), new BasicBlock(1, 4, 8), new BasicBlock(2, 8, 12)],
        Edges = [(0, 1), (0, 2), (1, 2)],
    };
}
