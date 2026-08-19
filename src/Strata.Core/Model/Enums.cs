namespace Strata.Core.Model;

/// <summary>Native container format of a <see cref="ScanTarget"/> (FR-001).</summary>
public enum BinaryFormat
{
    Unknown = 0,
    Elf,
    Pe,
    MachO,
}

/// <summary>Instruction-set architecture detected in a target (FR-003).</summary>
public enum Architecture
{
    Unknown = 0,
    X86_64,
    AArch64,
    Other,
}

/// <summary>How the target was linked; affects evidence weighting.</summary>
public enum Linkage
{
    Unknown = 0,
    Static,
    Dynamic,
    Mixed,
}

/// <summary>Packing/obfuscation status. Strata detects and flags; it never unpacks (FR-005).</summary>
public enum PackingStatus
{
    NotPacked = 0,
    Suspected,
    Packed,
}

/// <summary>How a version was pinned (FR-013).</summary>
public enum VersionKind
{
    Exact = 0,
    Range,
}

/// <summary>Concrete kind of a piece of evidence (FR-014, Principle XII).</summary>
public enum EvidenceKind
{
    MatchedString = 0,
    MatchedConstant,
    MatchedFunction,
    PresentSymbol,
    VersionString,
}

/// <summary>Which fingerprint signal produced a match (research.md R5).</summary>
public enum SignalKind
{
    StringConstant = 0,
    CfgShape,
    NormInsn,
    Embedding,
    Symbol,
}

/// <summary>Why a code region could not be attributed to a corpus library (FR-015).</summary>
public enum UnidentifiedReason
{
    NoMatch = 0,
    LowConfidence,
    RecoveryUncertain,
    Packed,
}
