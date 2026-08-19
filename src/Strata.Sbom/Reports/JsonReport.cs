using System.Text.Json;
using Strata.Core.Model;

namespace Strata.Sbom.Reports;

/// <summary>
/// Machine-readable report (FR-017): the full <see cref="ScanResult"/> serialised — components,
/// evidence, version basis, unidentified regions, warnings, and corpus/tool versions.
/// </summary>
public static class JsonReport
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Render(ScanResult result) => JsonSerializer.Serialize(result, Options);
}
