using Strata.Core.Disassembly;
using Strata.Core.Model;

namespace Strata.Core.Recovery;

/// <summary>
/// Builds a basic-block control-flow graph from a function's linear instruction stream (FR-006). Calls
/// are treated as sequential (control returns); conditional/unconditional/indirect branches, returns
/// and interrupts terminate a block. The shape (not the addresses) is what fingerprinting consumes.
/// </summary>
public static class CfgBuilder
{
    public static (IReadOnlyList<BasicBlock> Blocks, IReadOnlyList<(int From, int To)> Edges) Build(
        IReadOnlyList<DecodedInstruction> instructions)
    {
        if (instructions.Count == 0)
        {
            return ([], []);
        }

        ulong funcStart = instructions[0].Address;
        ulong funcEnd = instructions[^1].Address + (ulong)instructions[^1].Length;

        // Leaders: first instruction, branch targets in-range, and instructions following a terminator.
        var leaders = new SortedSet<ulong> { funcStart };
        for (int i = 0; i < instructions.Count; i++)
        {
            DecodedInstruction ins = instructions[i];
            if (!IsTerminator(ins.Flow))
            {
                continue;
            }

            if (IsBranch(ins.Flow) && ins.BranchTarget >= funcStart && ins.BranchTarget < funcEnd)
            {
                leaders.Add(ins.BranchTarget);
            }

            ulong next = ins.Address + (ulong)ins.Length;
            if (next < funcEnd)
            {
                leaders.Add(next);
            }
        }

        List<ulong> leaderList = leaders.ToList();
        var blockIndexByStart = new Dictionary<ulong, int>();
        var blocks = new List<BasicBlock>();
        for (int b = 0; b < leaderList.Count; b++)
        {
            ulong start = leaderList[b];
            ulong end = b + 1 < leaderList.Count ? leaderList[b + 1] : funcEnd;
            blockIndexByStart[start] = b;
            blocks.Add(new BasicBlock(b, start, end));
        }

        // Last instruction of each block, computed in ONE linear pass (blocks are address-sorted).
        var lastByBlock = new DecodedInstruction?[blocks.Count];
        int bi = 0;
        foreach (DecodedInstruction ins in instructions)
        {
            while (bi + 1 < blocks.Count && ins.Address >= blocks[bi + 1].StartAddress)
            {
                bi++;
            }

            if (ins.Address >= blocks[bi].StartAddress && ins.Address < blocks[bi].EndAddress)
            {
                lastByBlock[bi] = ins;
            }
        }

        var edges = new List<(int, int)>();
        for (int b = 0; b < blocks.Count; b++)
        {
            if (lastByBlock[b] is not { } lastIns)
            {
                continue;
            }

            ulong fallthrough = lastIns.Address + (ulong)lastIns.Length;

            switch (lastIns.Flow)
            {
                case FlowKind.Return:
                case FlowKind.Interrupt:
                    break; // no successors
                case FlowKind.UnconditionalBranch:
                    AddEdge(edges, blockIndexByStart, b, lastIns.BranchTarget);
                    break;
                case FlowKind.ConditionalBranch:
                    AddEdge(edges, blockIndexByStart, b, lastIns.BranchTarget);
                    AddEdge(edges, blockIndexByStart, b, fallthrough);
                    break;
                default:
                    AddEdge(edges, blockIndexByStart, b, fallthrough);
                    break;
            }
        }

        return (blocks, edges);
    }

    private static void AddEdge(List<(int, int)> edges, Dictionary<ulong, int> byStart, int from, ulong toAddr)
    {
        if (byStart.TryGetValue(toAddr, out int to))
        {
            edges.Add((from, to));
        }
    }

    private static bool IsTerminator(FlowKind f) => f is
        FlowKind.ConditionalBranch or FlowKind.UnconditionalBranch or FlowKind.IndirectBranch
        or FlowKind.Return or FlowKind.Interrupt;

    private static bool IsBranch(FlowKind f) => f is
        FlowKind.ConditionalBranch or FlowKind.UnconditionalBranch;
}
