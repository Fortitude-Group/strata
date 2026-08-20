using Strata.Core.Model;

namespace Strata.Sbom;

/// <summary>SBOM formats Strata can emit (FR-016).</summary>
public enum SbomFormat
{
    CycloneDx,
    Spdx,
}

/// <summary>
/// Facade over the emitters that also enforces the FR-018 guard: Strata output must never assert CRA
/// compliance. This is a hard invariant, checked on every emitted document (contracts/sbom-output.md
/// invariant 5).
/// </summary>
public static class SbomWriter
{
    private static readonly string[] ForbiddenClaims =
    [
        "cra compliant",
        "cra-compliant",
        "cra compliance certified",
        "makes you compliant",
        "make you compliant",
        "makes your product compliant",
        "fully compliant with the cra",
    ];

    public static string Emit(ScanResult result, SbomFormat format, SbomOutputOptions options)
    {
        string output = format switch
        {
            SbomFormat.CycloneDx => CycloneDxEmitter.Emit(result, options),
            SbomFormat.Spdx => SpdxEmitter.Emit(result, options),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        AssertNoComplianceClaims(output);
        return output;
    }

    /// <summary>FR-018: throws if the text asserts CRA compliance. Used by contract tests too.</summary>
    public static void AssertNoComplianceClaims(string text)
    {
        string lower = text.ToLowerInvariant();
        foreach (string claim in ForbiddenClaims)
        {
            if (lower.Contains(claim, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"FR-018 violation: output asserts compliance (\"{claim}\"). Strata produces evidence, not compliance.");
            }
        }
    }
}
