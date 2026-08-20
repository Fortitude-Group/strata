using Strata.Core;
using Strata.Core.Corpus;
using Strata.Core.Model;
using Strata.Corpus;

namespace Strata.Web.Services;

/// <summary>One of the bundled sample binaries offered without an upload (FR-024, contracts/web-demo.md).</summary>
public sealed record SampleBinary(string Id, string FileName, string DisplayName, string Description);

/// <summary>
/// Runs the shared Strata engine for the web demo (Principle I: the demo and the CLI use the same
/// <see cref="IScanner"/>, so identical input yields identical output — contract invariant 4). Holds the
/// seed corpus and the bundled sample catalog; carries no per-request state.
/// </summary>
public sealed class ScanDemoService
{
    private readonly IWebHostEnvironment _env;
    private readonly ICorpus _corpus = SeedCorpus.AsCorpus();

    public ScanDemoService(IWebHostEnvironment env)
    {
        _env = env;
    }

    /// <summary>
    /// The three bundled samples (FR-024, FR-026: no fleet/vehicle/telematics content). Each is a
    /// minimal but structurally valid ELF64 blob with appended library-signature strings the seed corpus
    /// recognises, plus one unrecognised string so the demo also shows an unidentified region.
    /// </summary>
    public IReadOnlyList<SampleBinary> Samples { get; } =
    [
        new(
            "router-firmware",
            "router-firmware.dat",
            "Router firmware extract",
            "A small firmware-style blob bundling zlib (full-confidence, exact version)."),
        new(
            "static-multilib",
            "static-multilib.dat",
            "Stripped static multi-library hello-world",
            "A statically-linked demo bundling OpenSSL, libpng, and a partial SQLite match."),
        new(
            "vendor",
            "vendor.dat",
            "Vendor-style DLL",
            "A vendor-supplied module: a low-confidence partial zlib match alongside full-confidence OpenSSL."),
    ];

    /// <summary>
    /// Opens a bundled sample as an in-memory stream. Samples live under wwwroot for static serving too,
    /// but scanning always reads them straight into memory — the same retention story as an upload.
    /// </summary>
    public async Task<(Stream Binary, string Name)> OpenSampleAsync(string sampleId, CancellationToken cancellationToken)
    {
        SampleBinary sample = Samples.FirstOrDefault(s => s.Id == sampleId)
            ?? throw new ArgumentException($"Unknown sample '{sampleId}'.", nameof(sampleId));

        string path = Path.Combine(_env.WebRootPath, "samples", sample.FileName);
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return (new MemoryStream(bytes, writable: false), sample.FileName);
    }

    /// <summary>
    /// Runs a full scan against the seed corpus (FR-024). A fresh <see cref="StrataScanner"/> is used per
    /// call — it holds no state, so this is just cheap isolation between concurrent demo users.
    /// </summary>
    public ScanResult Scan(Stream binary, string name, long maxUploadBytes, IProgress<ScanProgress>? progress)
    {
        var options = new ScanOptions
        {
            Load = new LoadOptions { MaxInputBytes = maxUploadBytes },
        };

        StrataScanner scanner = new();
        return scanner.Scan(binary, name, _corpus, options, progress);
    }
}
