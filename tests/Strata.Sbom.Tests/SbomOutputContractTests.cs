using System.Text.Json;
using Strata.Core.Model;

namespace Strata.Sbom.Tests;

public sealed class SbomOutputContractTests
{
    private static ScanResult SampleResult()
    {
        var evidence = new[]
        {
            new EvidenceRecord
            {
                Kind = EvidenceKind.VersionString,
                Signal = SignalKind.StringConstant,
                Detail = "matched string \"deflate 1.2.11\"",
                Strength = 1.0,
            },
        };

        var component = new IdentifiedComponent(
            "zlib",
            VersionResolution.OfExact("1.2.11", evidence),
            0.95,
            evidence,
            purl: "pkg:generic/zlib",
            knownLicense: "Zlib");

        var target = new ScanTarget
        {
            Name = "sample.elf",
            SizeBytes = 4096,
            Format = BinaryFormat.Elf,
            Architecture = Architecture.X86_64,
        };

        return new ScanResult
        {
            Target = target,
            Components = [component],
            UnidentifiedRegions = [new UnidentifiedRegion { StartAddress = 0, EndAddress = 4096, Reason = UnidentifiedReason.NoMatch }],
            CorpusVersion = "seed-0.1.0",
            ToolVersion = "0.1.0",
        };
    }

    [Fact]
    public void CycloneDx_output_is_valid_json_with_evidence_and_confidence()
    {
        string json = SbomWriter.Emit(SampleResult(), SbomFormat.CycloneDx, new SbomOutputOptions { Deterministic = true });
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.Equal("CycloneDX", doc.RootElement.GetProperty("bomFormat").GetString());
        Assert.Equal("1.6", doc.RootElement.GetProperty("specVersion").GetString());
        Assert.Contains("strata:confidence", json, StringComparison.Ordinal);
        Assert.Contains("strata:evidence", json, StringComparison.Ordinal);
        Assert.Contains("strata:unidentifiedRegion", json, StringComparison.Ordinal);
        Assert.Contains("zlib", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Spdx_output_is_valid_json_23()
    {
        string json = SbomWriter.Emit(SampleResult(), SbomFormat.Spdx, new SbomOutputOptions { Deterministic = true });
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.Equal("SPDX-2.3", doc.RootElement.GetProperty("spdxVersion").GetString());
        Assert.True(doc.RootElement.GetProperty("packages").GetArrayLength() >= 1);
    }

    [Fact]
    public void Deterministic_mode_is_byte_identical_across_runs()
    {
        var opts = new SbomOutputOptions { Deterministic = true };
        string a = SbomWriter.Emit(SampleResult(), SbomFormat.CycloneDx, opts);
        string b = SbomWriter.Emit(SampleResult(), SbomFormat.CycloneDx, opts);
        Assert.Equal(a, b);   // Principle IV / contracts/sbom-output.md invariant 6
    }

    [Fact]
    public void Fr018_guard_rejects_compliance_claims_but_passes_our_output()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SbomWriter.AssertNoComplianceClaims("This SBOM makes you compliant with the CRA."));

        // Our real output must pass the guard.
        string json = SbomWriter.Emit(SampleResult(), SbomFormat.CycloneDx, new SbomOutputOptions { Deterministic = true });
        SbomWriter.AssertNoComplianceClaims(json);   // does not throw
    }
}
