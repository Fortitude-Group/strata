using System;

namespace Strata.Sbom;

/// <summary>
/// Controls nondeterministic SBOM fields (Principle IV). In deterministic mode the serial number and
/// timestamp are omitted so two runs on the same (binary, corpus) are byte-identical (SC-006 golden
/// tests, contracts/sbom-output.md invariant 6).
/// </summary>
public sealed record SbomOutputOptions
{
    public bool Deterministic { get; init; }

    /// <summary>UUID used for the document serial number when not deterministic.</summary>
    public string SerialNumber { get; init; } = Guid.NewGuid().ToString();

    /// <summary>Document timestamp when not deterministic.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
