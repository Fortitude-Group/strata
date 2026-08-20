using Strata.Core.Disassembly;
using Strata.Core.Model;

namespace Strata.Core.Recovery;

/// <summary>
/// First-party function-boundary recovery (FR-006, research.md R4). Fast path: linear-sweep decode of
/// executable sections, with call targets and section starts as function entry points. Emits a
/// recovery confidence per function; symbol-driven and prologue heuristics layer on here, and the
/// optional Ghidra/radare2 plug-in supplies boundaries for low-confidence regions.
/// </summary>
public sealed class FunctionRecovery : IFunctionRecovery
{
    public IReadOnlyList<RecoveredFunction> Recover(ScanTarget target, RecoveryOptions options)
    {
        ReadOnlyMemory<byte> image = target.Image;
        IInstructionDecoder? decoder = DecoderFactory.For(target.Architecture);
        if (decoder is null || image.IsEmpty)
        {
            return [];
        }

        var functions = new List<RecoveredFunction>();
        int nextId = 0;

        foreach (Section section in ExecutableSections(target))
        {
            if (section.Offset + section.Size > (ulong)image.Length || section.Size == 0)
            {
                continue;
            }

            ReadOnlyMemory<byte> code = image.Slice((int)section.Offset, (int)section.Size);
            IReadOnlyList<DecodedInstruction> instrs = decoder.Decode(code, section.VirtualAddress);
            if (instrs.Count == 0)
            {
                continue;
            }

            List<ulong> entries = EntryPoints(instrs, section);
            for (int e = 0; e < entries.Count; e++)
            {
                ulong start = entries[e];
                ulong end = e + 1 < entries.Count ? entries[e + 1] : section.VirtualAddress + section.Size;
                List<DecodedInstruction> body = instrs.Where(i => i.Address >= start && i.Address < end).ToList();
                if (body.Count == 0)
                {
                    continue;
                }

                (IReadOnlyList<BasicBlock> blocks, IReadOnlyList<(int, int)> edges) = CfgBuilder.Build(body);
                functions.Add(new RecoveredFunction
                {
                    Id = nextId++,
                    StartAddress = start,
                    EndAddress = end,
                    BasicBlocks = blocks,
                    Edges = edges,
                    Mnemonics = body.Select(i => i.Mnemonic).ToList(),
                    RecoveryConfidence = 0.6, // linear-sweep confidence; symbol-driven recovery raises this
                });
            }
        }

        return functions;
    }

    private static IEnumerable<Section> ExecutableSections(ScanTarget target)
    {
        var named = target.Sections
            .Where(s => s.Name is ".text" or "text" or ".init" or ".plt" or "__text")
            .ToList();
        return named.Count > 0 ? named : target.Sections.Where(s => s.Size > 0);
    }

    private static List<ulong> EntryPoints(IReadOnlyList<DecodedInstruction> instrs, Section section)
    {
        var entries = new SortedSet<ulong> { section.VirtualAddress };
        ulong lo = section.VirtualAddress;
        ulong hi = section.VirtualAddress + section.Size;
        foreach (DecodedInstruction i in instrs)
        {
            if (i.Flow == FlowKind.Call && i.BranchTarget >= lo && i.BranchTarget < hi)
            {
                entries.Add(i.BranchTarget);
            }
        }

        return entries.ToList();
    }
}
