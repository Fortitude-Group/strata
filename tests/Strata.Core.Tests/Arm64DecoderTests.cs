using System.Collections.Generic;
using Strata.Core.Disassembly;
using Strata.Core.Model;
using Xunit;

namespace Strata.Core.Tests;

public sealed class Arm64DecoderTests
{
    [Fact]
    public void Decodes_aarch64_ret()
    {
        // AArch64 `ret` = 0xD65F03C0, little-endian on disk: C0 03 5F D6
        byte[] code = [0xC0, 0x03, 0x5F, 0xD6];
        IReadOnlyList<DecodedInstruction> instrs = new CapstoneArm64Decoder().Decode(code, 0x1000);

        DecodedInstruction only = Assert.Single(instrs);
        Assert.Equal("ret", only.Mnemonic);
        Assert.Equal(FlowKind.Return, only.Flow);
        Assert.Equal(4, only.Length);
    }

    [Fact]
    public void Decodes_aarch64_sequence_with_mnemonics()
    {
        // mov w0, #1  (52800020) ; ret (D65F03C0)
        byte[] code = [0x20, 0x00, 0x80, 0x52, 0xC0, 0x03, 0x5F, 0xD6];
        IReadOnlyList<DecodedInstruction> instrs = new CapstoneArm64Decoder().Decode(code, 0x2000);

        Assert.Equal(2, instrs.Count);
        Assert.Equal((ulong)0x2000, instrs[0].Address);
        Assert.Equal("movz", instrs[0].Mnemonic);   // Capstone canonicalises `mov #imm` to its `movz` form
        Assert.Equal(FlowKind.Return, instrs[1].Flow);
    }

    [Fact]
    public void DecoderFactory_returns_capstone_for_aarch64()
    {
        IInstructionDecoder? decoder = DecoderFactory.For(Architecture.AArch64);
        Assert.NotNull(decoder);
        Assert.Equal(Architecture.AArch64, decoder!.Architecture);
    }
}
