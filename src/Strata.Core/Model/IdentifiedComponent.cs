using System;
using System.Collections.Generic;
using System.Linq;

namespace Strata.Core.Model;

/// <summary>
/// A library attributed to the target — the SBOM component (FR-012/014). The constructor enforces the
/// evidence invariant (SC-007): a component with no evidence MUST NOT exist.
/// </summary>
public sealed record IdentifiedComponent
{
    public IdentifiedComponent(
        string libraryName,
        VersionResolution version,
        double confidence,
        IReadOnlyList<EvidenceRecord> evidence,
        string? purl = null,
        string? knownLicense = null,
        IReadOnlyList<VulnerabilityReference>? vulnerabilities = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(libraryName);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Count == 0)
        {
            // FR-014 / SC-007: honesty invariant — no claim without a basis.
            throw new ArgumentException(
                $"IdentifiedComponent '{libraryName}' has no evidence; components without evidence are forbidden (FR-014).",
                nameof(evidence));
        }

        LibraryName = libraryName;
        Version = version;
        Confidence = confidence;
        Evidence = evidence;
        Purl = purl;
        KnownLicense = knownLicense;
        Vulnerabilities = vulnerabilities ?? [];
    }

    public string LibraryName { get; }

    public VersionResolution Version { get; }

    /// <summary>0..1 distinctiveness-weighted coverage confidence (research.md R9).</summary>
    public double Confidence { get; }

    public IReadOnlyList<EvidenceRecord> Evidence { get; }

    public string? Purl { get; }

    public string? KnownLicense { get; }

    public IReadOnlyList<VulnerabilityReference> Vulnerabilities { get; init; }

    public IdentifiedComponent WithVulnerabilities(IReadOnlyList<VulnerabilityReference> vulns) =>
        this with { Vulnerabilities = vulns };

    /// <summary>Evidence grouped by signal, for reporting.</summary>
    public IReadOnlyDictionary<SignalKind, int> EvidenceBySignal =>
        Evidence.GroupBy(e => e.Signal).ToDictionary(g => g.Key, g => g.Count());
}
