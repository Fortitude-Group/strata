using Strata.Core;
using Strata.Core.Corpus;
using Strata.Core.Model;
using Strata.Corpus;

namespace Strata.Cli.Tests;

/// <summary>
/// End-to-end US1 slice (quickstart Scenarios 1 &amp; 3): a stripped ELF containing known library strings,
/// scanned against the SQLite seed corpus, yields an evidence-backed result with an unidentified section.
/// </summary>
public sealed class ScanEndToEndTests : IDisposable
{
    private readonly string _dir;
    private readonly string _corpusDb;

    public ScanEndToEndTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "strata-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _corpusDb = Path.Combine(_dir, "seed.db");
        SeedCorpus.BuildDatabase(_corpusDb);   // task T021 path
    }

    [Fact]
    public void Scans_elf_and_identifies_seed_libraries_with_evidence()
    {
        byte[] elf = TestBinaries.MakeElfWithStrings(
        [
            "deflate 1.2.11 Copyright 1995-2017 Jean-loup Gailly and Mark Adler ",
            "OpenSSL 1.1.1k  25 Mar 2021",
            "some vendor-specific blob that matches nothing",
        ]);

        ICorpus corpus = SqliteCorpus.Load(_corpusDb);
        var scanner = new StrataScanner();

        ScanResult result;
        using (var ms = new MemoryStream(elf))
        {
            result = scanner.Scan(ms, "fixture.elf", corpus, new ScanOptions());
        }

        Assert.Equal(BinaryFormat.Elf, result.Target.Format);
        Assert.Equal(Architecture.X86_64, result.Target.Architecture);

        IdentifiedComponent zlib = Assert.Single(result.Components, c => c.LibraryName == "zlib");
        Assert.Equal("1.2.11", zlib.Version.Exact);
        Assert.All(result.Components, c => Assert.NotEmpty(c.Evidence));   // FR-014 / SC-007

        Assert.Contains(result.Components, c => c.LibraryName == "openssl");
        Assert.NotEmpty(result.UnidentifiedRegions);   // SC-008 — the vendor blob is reported, not hidden
    }

    [Fact]
    public void Unrecognised_input_is_rejected_via_scan_command()
    {
        string txt = Path.Combine(_dir, "notbinary.txt");
        File.WriteAllText(txt, "this is plainly not a native binary");

        ArgMap args = ArgMap.Parse(["scan", txt, "--report", "none"], 1);
        var err = new StringWriter();
        int code = ScanCommand.Run(args, new StringWriter(), err);

        Assert.Equal(ExitCodes.Error, code);       // FR-004
        Assert.Contains("not a recognised native binary", err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_flags_known_cve_and_returns_findings_exit_code()
    {
        byte[] elf = TestBinaries.MakeElfWithStrings(["OpenSSL 1.1.1k  25 Mar 2021"]);
        string bin = Path.Combine(_dir, "ssl.elf");
        File.WriteAllBytes(bin, elf);
        string sbom = Path.Combine(_dir, "ssl.sbom");

        var stdout = new StringWriter();
        ArgMap args = ArgMap.Parse(
            ["scan", bin, "--corpus", _corpusDb, "--output", sbom, "--report", "json", "--deterministic"], 1);
        int code = ScanCommand.Run(args, stdout, new StringWriter());

        Assert.Equal(ExitCodes.FindingsNeedAttention, code);          // US5: CVE present → exit 2 (FR-019/FR-020)
        Assert.Contains("CVE-2021-3712", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Deterministic_scan_produces_identical_sbom_bytes()
    {
        byte[] elf = TestBinaries.MakeElfWithStrings(["deflate 1.2.11 Copyright 1995-2017 Jean-loup Gailly and Mark Adler "]);
        string bin = Path.Combine(_dir, "det.elf");
        File.WriteAllBytes(bin, elf);
        string outA = Path.Combine(_dir, "a.json");
        string outB = Path.Combine(_dir, "b.json");

        RunScan(bin, outA);
        RunScan(bin, outB);

        Assert.Equal(File.ReadAllText(outA), File.ReadAllText(outB));   // Principle IV
    }

    private void RunScan(string bin, string outPath)
    {
        ArgMap args = ArgMap.Parse(
            ["scan", bin, "--corpus", _corpusDb, "--format", "cyclonedx", "--output", outPath, "--report", "none", "--deterministic", "--vuln", "off"],
            1);
        int code = ScanCommand.Run(args, new StringWriter(), new StringWriter());
        Assert.Equal(ExitCodes.Success, code);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
    }
}
