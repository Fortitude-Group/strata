using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Strata.Vuln.Model;

/// <summary>
/// Reads the bundled OSV-style snapshot (<c>Data/osv-snapshot.json</c>, embedded resource) exactly
/// once and caches it for the process lifetime — no network, no filesystem, deterministic (Principle IV).
/// </summary>
public static class VulnSnapshotLoader
{
    private const string ResourceFileName = "osv-snapshot.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Lazy<VulnSnapshot> LazySnapshot =
        new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The bundled snapshot, parsed once and cached.</summary>
    public static VulnSnapshot Snapshot => LazySnapshot.Value;

    private static VulnSnapshot Load()
    {
        var assembly = typeof(VulnSnapshotLoader).Assembly;
        string? resourceName = Array.Find(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith(ResourceFileName, StringComparison.Ordinal));

        if (resourceName is null)
        {
            throw new InvalidOperationException(
                $"Embedded OSV snapshot resource '{ResourceFileName}' was not found in {assembly.FullName}.");
        }

        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded resource stream '{resourceName}'.");

        return JsonSerializer.Deserialize<VulnSnapshot>(stream, SerializerOptions)
            ?? throw new InvalidOperationException($"Embedded OSV snapshot '{resourceName}' deserialized to null.");
    }
}
