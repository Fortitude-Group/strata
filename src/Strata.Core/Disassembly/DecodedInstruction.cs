namespace Strata.Core.Disassembly;

/// <summary>Control-flow category of a decoded instruction (abstracted across ISAs).</summary>
public enum FlowKind
{
    Sequential = 0,
    Call,
    IndirectCall,
    ConditionalBranch,
    UnconditionalBranch,
    IndirectBranch,
    Return,
    Interrupt,
}

/// <summary>
/// A single decoded instruction, abstracted to what fingerprinting needs (research.md R5): its address,
/// length, normalised mnemonic (operands abstracted away — robust to register allocation), and control
/// flow. Concrete decoding is provided per-architecture behind <see cref="IInstructionDecoder"/>.
/// </summary>
public readonly record struct DecodedInstruction(
    ulong Address,
    int Length,
    string Mnemonic,
    FlowKind Flow,
    ulong BranchTarget);
