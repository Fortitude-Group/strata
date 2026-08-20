using Strata.Core.Model;

namespace Strata.Core.Disassembly;

/// <summary>
/// Decodes a byte range into a linear instruction stream (research.md R2). x86-64 uses the pure-C#
/// Iced decoder (no native dependency); AArch64 uses a Capstone binding (added with T034).
/// </summary>
public interface IInstructionDecoder
{
    Architecture Architecture { get; }

    /// <summary>Linear-sweep decode of <paramref name="code"/>, whose first byte is at <paramref name="baseAddress"/>.</summary>
    IReadOnlyList<DecodedInstruction> Decode(ReadOnlyMemory<byte> code, ulong baseAddress);
}

/// <summary>Selects a decoder for a target architecture, or null when unsupported.</summary>
public static class DecoderFactory
{
    public static IInstructionDecoder? For(Architecture architecture) => architecture switch
    {
        Architecture.X86_64 => new IcedX64Decoder(),
        _ => null, // AArch64 (Capstone) lands with T034; others are out of scope.
    };
}
