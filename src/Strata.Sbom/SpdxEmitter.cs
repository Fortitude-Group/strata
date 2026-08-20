using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Strata.Core.Model;

namespace Strata.Sbom;

/// <summary>
/// First-party SPDX 2.3 (JSON) emitter (FR-016). Evidence + confidence ride in package comments;
/// unidentified regions ride in document annotations (contracts/sbom-output.md).
/// </summary>
public static class SpdxEmitter
{
    public static string Emit(ScanResult result, SbomOutputOptions options)
    {
        var packages = new JsonArray();
        int i = 0;
        foreach (IdentifiedComponent c in result.Components)
        {
            var evidenceLines = new List<string>();
            foreach (EvidenceRecord e in c.Evidence)
            {
                evidenceLines.Add($"[{e.Signal}] {e.Detail}");
            }

            string vulnPart = c.Vulnerabilities.Count > 0
                ? "; vulnerabilities=" + string.Join(
                    ",", c.Vulnerabilities.Select(v => v.AppliesToRange ? $"{v.Id}(range)" : v.Id))
                : string.Empty;

            var pkg = new JsonObject
            {
                ["name"] = c.LibraryName,
                ["SPDXID"] = $"SPDXRef-Package-{Sanitize(c.LibraryName)}-{i}",
                ["versionInfo"] = c.Version.Display,
                ["downloadLocation"] = "NOASSERTION",
                ["licenseDeclared"] = string.IsNullOrEmpty(c.KnownLicense) ? "NOASSERTION" : c.KnownLicense,
                ["licenseConcluded"] = "NOASSERTION",
                ["copyrightText"] = "NOASSERTION",
                ["comment"] =
                    $"strata:confidence={c.Confidence.ToString("F3", CultureInfo.InvariantCulture)}; " +
                    $"versionKind={c.Version.Kind}; evidence={string.Join(" | ", evidenceLines)}" + vulnPart,
            };
            packages.Add(pkg);
            i++;
        }

        var annotations = new JsonArray();
        foreach (UnidentifiedRegion region in result.UnidentifiedRegions)
        {
            annotations.Add(new JsonObject
            {
                ["annotationType"] = "OTHER",
                ["annotator"] = $"Tool: strata-{result.ToolVersion}",
                ["comment"] = $"strata:unidentifiedRegion 0x{region.StartAddress:x}-0x{region.EndAddress:x} ({region.Reason})",
            });
        }

        var creationInfo = new JsonObject
        {
            ["creators"] = new JsonArray($"Tool: strata-{result.ToolVersion}", "Organization: Fortitude Omnis Group"),
        };
        if (!options.Deterministic)
        {
            creationInfo["created"] = options.Timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        var doc = new JsonObject
        {
            ["spdxVersion"] = "SPDX-2.3",
            ["dataLicense"] = "CC0-1.0",
            ["SPDXID"] = "SPDXRef-DOCUMENT",
            ["name"] = $"strata-sbom-{Sanitize(result.Target.Name)}",
            ["documentNamespace"] = options.Deterministic
                ? "https://fortitude-omnis.group/strata/deterministic"
                : $"https://fortitude-omnis.group/strata/{options.SerialNumber}",
            ["creationInfo"] = creationInfo,
            ["packages"] = packages,
            ["annotations"] = annotations,
        };

        return doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string Sanitize(string s)
    {
        var chars = s.ToCharArray();
        for (int j = 0; j < chars.Length; j++)
        {
            if (!char.IsLetterOrDigit(chars[j]) && chars[j] != '-' && chars[j] != '.')
            {
                chars[j] = '-';
            }
        }

        return new string(chars);
    }
}
