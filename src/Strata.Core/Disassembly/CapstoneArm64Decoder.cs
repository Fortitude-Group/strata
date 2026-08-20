using System.Collections.Generic;
using System.Globalization;
using Gee.External.Capstone;
using Gee.External.Capstone.Arm64;
using Strata.Core.Model;

namespace Strata.Core.Disassembly;

/// <summary>
/// AArch64 instruction decoder backed by Capstone (research.md R2, T034). Normalises each instruction to
/// its mnemonic + control-flow category — flow is derived from the well-defined AArch64 branch mnemonics
/// (robust, no reliance on optional instruction detail), and direct-branch targets are read from the
/// operand text.
/// </summary>
public sealed class CapstoneArm64Decoder : IInstructionDecoder
{
    public Architecture Architecture => Architecture.AArch64;

    public IReadOnlyList<DecodedInstruction> Decode(ReadOnlyMemory<byte> code, ulong baseAddress)
    {
        var results = new List<DecodedInstruction>();
        try
        {
            using CapstoneArm64Disassembler disassembler =
                CapstoneDisassembler.CreateArm64Disassembler(Arm64DisassembleMode.Arm);
            disassembler.EnableInstructionDetails = false;

            Arm64Instruction[] instructions = disassembler.Disassemble(code.ToArray(), (long)baseAddress);
            foreach (Arm64Instruction instr in instructions)
            {
                string mnemonic = instr.Mnemonic ?? string.Empty;
                (FlowKind flow, bool hasImmTarget) = ClassifyFlow(mnemonic);
                ulong target = hasImmTarget ? ParseImmediate(instr.Operand) : 0;

                results.Add(new DecodedInstruction(
                    Address: (ulong)instr.Address,
                    Length: instr.Bytes?.Length ?? 4,
                    Mnemonic: Normalise(mnemonic),
                    Flow: flow,
                    BranchTarget: target));
            }
        }
        catch (System.Exception)
        {
            // Capstone native failure must not abort the scan; degrade to no functions for this range.
            return results;
        }

        return results;
    }

    private static (FlowKind Flow, bool HasImmTarget) ClassifyFlow(string mnemonic)
    {
        string m = mnemonic.ToLowerInvariant();
        return m switch
        {
            "ret" => (FlowKind.Return, false),
            "bl" => (FlowKind.Call, true),
            "blr" => (FlowKind.IndirectCall, false),
            "br" => (FlowKind.IndirectBranch, false),
            "b" => (FlowKind.UnconditionalBranch, true),
            "brk" or "hlt" or "svc" or "hvc" or "smc" => (FlowKind.Interrupt, false),
            _ when m.StartsWith("b.", System.StringComparison.Ordinal) => (FlowKind.ConditionalBranch, true),
            "cbz" or "cbnz" or "tbz" or "tbnz" => (FlowKind.ConditionalBranch, true),
            _ => (FlowKind.Sequential, false),
        };
    }

    // The instruction's mnemonic is the normalised opcode (operands abstracted); we keep the base
    // mnemonic and drop condition suffixes so b.eq / b.ne fingerprint identically to a generic branch.
    private static string Normalise(string mnemonic)
    {
        int dot = mnemonic.IndexOf('.', System.StringComparison.Ordinal);
        return dot > 0 ? mnemonic[..dot] : mnemonic;
    }

    private static ulong ParseImmediate(string? operand)
    {
        if (string.IsNullOrEmpty(operand))
        {
            return 0;
        }

        int hash = operand.IndexOf('#', System.StringComparison.Ordinal);
        if (hash < 0 || hash + 1 >= operand.Length)
        {
            return 0;
        }

        string token = operand[(hash + 1)..].Trim();
        bool hex = token.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase);
        if (hex)
        {
            token = token[2..];
        }

        return ulong.TryParse(
            token,
            hex ? NumberStyles.HexNumber : NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out ulong value) ? value : 0;
    }
}
