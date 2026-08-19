using System.Collections.Generic;
using System.Globalization;
using CycloneDX;
using CycloneDX.Json;
using CycloneDX.Models;
using Strata.Core.Model;

namespace Strata.Sbom;

/// <summary>
/// Emits a CycloneDX 1.6 JSON SBOM from a <see cref="ScanResult"/> (FR-016), attaching per-component
/// confidence + evidence and a document-level unidentified-regions block (FR-014/015). The library's
/// own serializer validates the shape (research.md R12).
/// </summary>
public static class CycloneDxEmitter
{
    public static string Emit(ScanResult result, SbomOutputOptions options)
    {
        var bom = new Bom
        {
            SpecVersion = SpecificationVersion.v1_6,
            Version = 1,
            Metadata = new Metadata
            {
                Properties = BuildDocumentProperties(result),
            },
        };

        // Determinism (Principle IV): only stamp serial/timestamp when NOT in deterministic mode.
        if (!options.Deterministic)
        {
            bom.SerialNumber = "urn:uuid:" + options.SerialNumber;
            bom.Metadata.Timestamp = options.Timestamp;
        }

        bom.Components = [];
        foreach (IdentifiedComponent c in result.Components)
        {
            bom.Components.Add(ToComponent(c));
        }

        return Serializer.Serialize(bom);
    }

    private static Component ToComponent(IdentifiedComponent c)
    {
        var props = new List<Property>
        {
            new() { Name = "strata:confidence", Value = c.Confidence.ToString("F3", CultureInfo.InvariantCulture) },
        };

        if (c.Version.Kind == VersionKind.Range)
        {
            props.Add(new Property { Name = "strata:versionRange", Value = c.Version.Display });
        }

        foreach (EvidenceRecord e in c.Evidence)
        {
            props.Add(new Property
            {
                Name = "strata:evidence",
                Value = $"[{e.Signal}] {e.Detail} (strength {e.Strength.ToString("F2", CultureInfo.InvariantCulture)})",
            });
        }

        var component = new Component
        {
            Type = Component.Classification.Library,
            Name = c.LibraryName,
            Version = c.Version.Kind == VersionKind.Exact ? c.Version.Exact : null,
            Purl = c.Purl,
            Properties = props,
        };

        if (!string.IsNullOrEmpty(c.KnownLicense))
        {
            component.Licenses =
            [
                new LicenseChoice { License = new License { Id = c.KnownLicense } },
            ];
        }

        return component;
    }

    private static List<Property> BuildDocumentProperties(ScanResult result)
    {
        var props = new List<Property>
        {
            new() { Name = "strata:toolVersion", Value = result.ToolVersion },
            new() { Name = "strata:corpusVersion", Value = result.CorpusVersion },
        };

        foreach (UnidentifiedRegion region in result.UnidentifiedRegions)
        {
            props.Add(new Property
            {
                Name = "strata:unidentifiedRegion",
                Value = $"0x{region.StartAddress:x}-0x{region.EndAddress:x} ({region.Reason})",
            });
        }

        foreach (string warning in result.Warnings)
        {
            props.Add(new Property { Name = "strata:warning", Value = warning });
        }

        return props;
    }
}
