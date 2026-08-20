using System.Collections.Generic;
using Iced.Intel;
using Strata.Core.Model;

namespace Strata.Core.Disassembly;

/// <summary>
/// x86-64 instruction decoder backed by Iced (pure C#, no native dependency, AOT-friendly —
/// research.md R2). Normalises each instruction to its mnemonic + control-flow category so downstream
/// fingerprints are robust to register allocation and immediate values.
/// </summary>
public sealed class IcedX64Decoder : IInstructionDecoder
{
    public Architecture Architecture => Architecture.X86_64;

    public IReadOnlyList<DecodedInstruction> Decode(ReadOnlyMemory<byte> code, ulong baseAddress)
    {
        var results = new List<DecodedInstruction>();
        byte[] bytes = code.ToArray();
        var reader = new ByteArrayCodeReader(bytes);
        var decoder = Decoder.Create(64, reader, baseAddress);

        while (reader.CanReadByte)
        {
            decoder.Decode(out Instruction instr);
            if (instr.IsInvalid || instr.Length == 0)
            {
                // Skip a byte and resync — linear sweep over stripped code hits data/padding.
                reader.Position = (int)(instr.IP - baseAddress) + 1;
                decoder.IP = instr.IP + 1;
                continue;
            }

            results.Add(new DecodedInstruction(
                Address: instr.IP,
                Length: instr.Length,
                Mnemonic: instr.Mnemonic.ToString(),
                Flow: MapFlow(instr.FlowControl),
                BranchTarget: IsNearBranch(instr) ? instr.NearBranchTarget : 0));
        }

        return results;
    }

    private static bool IsNearBranch(in Instruction instr) => instr.Op0Kind is
        OpKind.NearBranch16 or OpKind.NearBranch32 or OpKind.NearBranch64;

    private static FlowKind MapFlow(FlowControl fc) => fc switch
    {
        FlowControl.Call => FlowKind.Call,
        FlowControl.IndirectCall => FlowKind.IndirectCall,
        FlowControl.ConditionalBranch => FlowKind.ConditionalBranch,
        FlowControl.UnconditionalBranch => FlowKind.UnconditionalBranch,
        FlowControl.IndirectBranch => FlowKind.IndirectBranch,
        FlowControl.Return => FlowKind.Return,
        FlowControl.Interrupt => FlowKind.Interrupt,
        _ => FlowKind.Sequential,
    };
}
