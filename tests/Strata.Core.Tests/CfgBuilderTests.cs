using Strata.Core.Disassembly;
using Strata.Core.Model;
using Strata.Core.Recovery;

namespace Strata.Core.Tests;

public sealed class CfgBuilderTests
{
    [Fact]
    public void Straight_line_instructions_yield_a_single_block_with_no_internal_edges()
    {
        DecodedInstruction[] instrs =
        [
            new(0, 4, "push", FlowKind.Sequential, 0),
            new(4, 4, "mov", FlowKind.Sequential, 0),
            new(8, 4, "add", FlowKind.Sequential, 0),
        ];

        (IReadOnlyList<BasicBlock> blocks, IReadOnlyList<(int From, int To)> edges) = CfgBuilder.Build(instrs);

        BasicBlock only = Assert.Single(blocks);
        Assert.Equal(0ul, only.StartAddress);
        Assert.Equal(12ul, only.EndAddress);   // funcEnd = last addr + length
        Assert.Empty(edges);                    // fallthrough target is out-of-range -> no edge recorded
    }

    [Fact]
    public void Conditional_branch_splits_into_blocks_with_taken_and_fallthrough_edges()
    {
        // addr0: seq -> addr4: conditional branch to addr12 (fallthrough addr8) -> addr8: seq (falls to addr12)
        // -> addr12: return.
        DecodedInstruction[] instrs =
        [
            new(0, 4, "test", FlowKind.Sequential, 0),
            new(4, 4, "jz", FlowKind.ConditionalBranch, 12),
            new(8, 4, "mov", FlowKind.Sequential, 0),
            new(12, 4, "ret", FlowKind.Return, 0),
        ];

        (IReadOnlyList<BasicBlock> blocks, IReadOnlyList<(int From, int To)> edges) = CfgBuilder.Build(instrs);

        Assert.True(blocks.Count >= 2);
        Assert.Equal(3, blocks.Count);   // [0,8) [8,12) [12,16)

        int block0 = IndexOfBlockStarting(blocks, 0);
        int block1 = IndexOfBlockStarting(blocks, 8);
        int block2 = IndexOfBlockStarting(blocks, 12);

        // Conditional branch: both the taken edge (to addr12) and the fallthrough edge (to addr8) exist.
        Assert.Contains((block0, block2), edges);
        Assert.Contains((block0, block1), edges);
        // The fallthrough block falls through into the return block.
        Assert.Contains((block1, block2), edges);
        // The return block has no successors.
        Assert.DoesNotContain(edges, e => e.From == block2);
    }

    [Fact]
    public void Empty_instruction_list_yields_no_blocks_or_edges()
    {
        (IReadOnlyList<BasicBlock> blocks, IReadOnlyList<(int From, int To)> edges) = CfgBuilder.Build([]);

        Assert.Empty(blocks);
        Assert.Empty(edges);
    }

    private static int IndexOfBlockStarting(IReadOnlyList<BasicBlock> blocks, ulong start)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].StartAddress == start)
            {
                return blocks[i].Index;
            }
        }

        throw new InvalidOperationException($"no block starts at {start}");
    }
}
