using System.Globalization;
using System.Text;
using Strata.Core.Model;

namespace Strata.Sbom.Reports;

/// <summary>
/// Human-readable report (FR-017). Always ends with the unidentified / low-confidence section (FR-015)
/// so the tool is honest about what it could not place.
/// </summary>
public static class TextReport
{
    public static string Render(ScanResult result, double minConfidence)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Strata scan report — {result.Target.Name}");
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"  format={result.Target.Format} arch={result.Target.Architecture} linkage={result.Target.Linkage} " +
            $"size={result.Target.SizeBytes}B packing={result.Target.PackingStatus}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  tool={result.ToolVersion} corpus={result.CorpusVersion}");
        sb.AppendLine();

        sb.AppendLine("Identified components:");
        int shown = 0;
        foreach (IdentifiedComponent c in result.Components)
        {
            if (c.Confidence < minConfidence)
            {
                continue;
            }

            shown++;
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  • {c.LibraryName} {c.Version.Display}  (confidence {c.Confidence.ToString("P0", CultureInfo.InvariantCulture)})");
            foreach (EvidenceRecord e in c.Evidence)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"      - [{e.Signal}] {e.Detail}");
            }

            foreach (VulnerabilityReference v in c.Vulnerabilities)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"      ! {v.Id} ({v.Source}{(v.AppliesToRange ? ", range" : "")})");
            }
        }

        if (shown == 0)
        {
            sb.AppendLine("  (none above the confidence threshold)");
        }

        // Low-confidence components are listed, not hidden (FR-015).
        var lowConf = new StringBuilder();
        foreach (IdentifiedComponent c in result.Components)
        {
            if (c.Confidence < minConfidence)
            {
                lowConf.AppendLine(CultureInfo.InvariantCulture,
                    $"  • {c.LibraryName} {c.Version.Display}  (confidence {c.Confidence.ToString("P0", CultureInfo.InvariantCulture)})");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Low-confidence / unidentified regions:");
        if (lowConf.Length > 0)
        {
            sb.Append(lowConf);
        }

        if (result.UnidentifiedRegions.Count > 0)
        {
            foreach (UnidentifiedRegion r in result.UnidentifiedRegions)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"  • unattributed content 0x{r.StartAddress:x}–0x{r.EndAddress:x} ({r.Reason}) — precise per-function bounds pending function recovery");
            }
        }

        if (lowConf.Length == 0 && result.UnidentifiedRegions.Count == 0)
        {
            sb.AppendLine("  (all recovered content attributed)");
        }

        foreach (string w in result.Warnings)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  ⚠ {w}");
        }

        sb.AppendLine();
        sb.AppendLine("Note: Strata produces evidence toward an SBOM; it does not certify or confer CRA compliance.");
        sb.AppendLine("Compliance remains the user's responsibility.");
        return sb.ToString();
    }
}
