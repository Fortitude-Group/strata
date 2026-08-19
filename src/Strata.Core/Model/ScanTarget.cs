using System.Collections.Generic;

namespace Strata.Core.Model;

/// <summary>A section within a native binary.</summary>
public sealed record Section(string Name, ulong Offset, ulong Size, ulong VirtualAddress);

/// <summary>An imported or exported symbol (present only when the binary is not fully stripped).</summary>
public sealed record Symbol(string Name, ulong Address, bool IsImport);

/// <summary>A printable string recovered from the target, with the file offset it was found at.</summary>
public sealed record StringLiteral(string Value, ulong Offset);

/// <summary>A run of constant bytes (e.g. a lookup table or magic) recovered from the target.</summary>
public sealed record ConstantBlob( ulong Offset, byte[] Bytes);

/// <summary>
/// The input artefact under analysis after ingestion/parsing (FR-001..005). Immutable snapshot of what
/// the loader extracted; downstream stages read it but never mutate it (Principle IV determinism).
/// </summary>
public sealed record ScanTarget
{
    public required string Name { get; init; }

    /// <summary>Total size of the input in bytes.</summary>
    public required long SizeBytes { get; init; }

    public required BinaryFormat Format { get; init; }

    public required Architecture Architecture { get; init; }

    public Linkage Linkage { get; init; } = Linkage.Unknown;

    public PackingStatus PackingStatus { get; init; } = PackingStatus.NotPacked;

    public IReadOnlyList<Section> Sections { get; init; } = [];

    public IReadOnlyList<ulong> EntryPoints { get; init; } = [];

    public IReadOnlyList<Symbol> Symbols { get; init; } = [];

    public IReadOnlyList<StringLiteral> Strings { get; init; } = [];

    public IReadOnlyList<ConstantBlob> Constants { get; init; } = [];
}
