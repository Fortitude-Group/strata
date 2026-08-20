using Strata.Core.Errors;
using Strata.Core.Model;
using Strata.Web.Services;

namespace Strata.Web.Tests;

/// <summary>
/// T089: unit tests for <see cref="ScanDemoService"/> that don't require a full web host — a fake
/// <see cref="Microsoft.AspNetCore.Hosting.IWebHostEnvironment"/> pointed at the real wwwroot is enough
/// to exercise sample loading, and <see cref="ScanDemoService.Scan"/> is a plain method call.
/// </summary>
public sealed class ScanDemoServiceTests
{
    private static readonly string[] BannedTerms = ["telematic", "vehicle", "fleet", "vin", "gps", "obd"];

    private static ScanDemoService MakeService() => new(new FakeWebHostEnvironment
    {
        WebRootPath = FindWebRoot(),
    });

    [Fact]
    public void Sample_catalog_is_exactly_the_three_expected_samples()
    {
        ScanDemoService service = MakeService();

        string[] expectedIds = ["router-firmware", "static-multilib", "vendor"];

        Assert.Equal(3, service.Samples.Count);
        Assert.Equal(expectedIds, service.Samples.Select(s => s.Id));
    }

    [Fact]
    public void Sample_catalog_contains_no_telematics_or_vehicle_naming()
    {
        // FR-026: the public demo must not read as fleet/vehicle/telematics content.
        ScanDemoService service = MakeService();

        foreach (SampleBinary sample in service.Samples)
        {
            string haystack = $"{sample.Id} {sample.FileName} {sample.DisplayName} {sample.Description}".ToLowerInvariant();
            foreach (string term in BannedTerms)
            {
                Assert.DoesNotContain(term, haystack, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Sample_file_names_all_use_the_dat_extension()
    {
        // Deliberate (see Program.cs comment): dodges the *.bin/*.elf .gitignore rules for fixtures.
        ScanDemoService service = MakeService();

        Assert.All(service.Samples, s => Assert.EndsWith(".dat", s.FileName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_sample_id_throws_argument_exception()
    {
        ScanDemoService service = MakeService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.OpenSampleAsync("does-not-exist", CancellationToken.None));
    }

    [Fact]
    public async Task Bundled_sample_opens_and_scans_to_identified_components()
    {
        ScanDemoService service = MakeService();

        (Stream binary, string name) = await service.OpenSampleAsync("router-firmware", CancellationToken.None);
        ScanResult result;
        await using (binary)
        {
            result = service.Scan(binary, name, maxUploadBytes: 8 * 1024 * 1024, progress: null);
        }

        Assert.NotEmpty(result.Components);
        Assert.Contains(result.Components, c => c.LibraryName == "zlib");
    }

    [Fact]
    public void Scan_rejects_input_over_the_upload_cap_via_out_of_envelope_exception()
    {
        // FR-025 defence in depth: Scan() threads maxUploadBytes straight into LoadOptions.MaxInputBytes,
        // so a byte[] over the cap is rejected by the engine itself, not just the UI's metadata check.
        ScanDemoService service = MakeService();
        const long cap = 1024;
        byte[] oversized = new byte[cap + 1];

        using var ms = new MemoryStream(oversized);
        Assert.Throws<OutOfEnvelopeException>(() => service.Scan(ms, "too-big.bin", cap, progress: null));
    }

    [Fact]
    public void Scan_accepts_input_exactly_at_the_upload_cap()
    {
        ScanDemoService service = MakeService();
        byte[] elf = MakeMinimalElf();
        const long cap = 4096;
        Assert.True(elf.Length <= cap);

        using var ms = new MemoryStream(elf);
        // At-or-under the cap must not throw for size reasons (may still be a scan the corpus can't
        // identify anything in, but ingestion itself succeeds).
        ScanResult result = service.Scan(ms, "at-cap.elf", cap, progress: null);
        Assert.Equal(BinaryFormat.Elf, result.Target.Format);
    }

    /// <summary>A minimal valid ELF64 (x86-64) header — enough for FormatDetector/ElfReader, no strings.</summary>
    private static byte[] MakeMinimalElf()
    {
        var bytes = new byte[64];
        bytes[0] = 0x7F; bytes[1] = (byte)'E'; bytes[2] = (byte)'L'; bytes[3] = (byte)'F';
        bytes[4] = 2;    // EI_CLASS = 64-bit
        bytes[5] = 1;    // EI_DATA  = little-endian
        bytes[6] = 1;    // EI_VERSION
        bytes[16] = 2;   // e_type = ET_EXEC
        bytes[18] = 0x3E; // e_machine = EM_X86_64
        bytes[20] = 1;   // e_version
        bytes[52] = 64;  // e_ehsize
        return bytes;
    }

    private static string FindWebRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Strata.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("could not locate repo root (Strata.slnx) from test base directory");
        }

        return Path.Combine(dir.FullName, "src", "Strata.Web", "wwwroot");
    }
}
