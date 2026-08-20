using Strata.Core;
using Strata.Core.Corpus;
using Strata.Core.Model;
using Strata.Corpus;

namespace Strata.Cli.Tests;

/// <summary>
/// T103: format detection + library identification also holds for PE and Mach-O targets, not just ELF
/// (the string signal scans the whole file regardless of container format).
/// </summary>
public sealed class PeAndMachOScanTests : IDisposable
{
    private readonly string _dir;
    private readonly string _corpusDb;

    public PeAndMachOScanTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "strata-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _corpusDb = Path.Combine(_dir, "seed.db");
        SeedCorpus.BuildDatabase(_corpusDb);
    }

    [Fact]
    public void Scans_pe_and_identifies_seed_library_with_evidence()
    {
        byte[] pe = TestBinaries.MakePeWithStrings(
        [
            "libpng version 1.6.37 - April 14, 2019",
            "some vendor-specific blob that matches nothing",
        ]);

        ICorpus corpus = SqliteCorpus.Load(_corpusDb);
        var scanner = new StrataScanner();

        ScanResult result;
        using (var ms = new MemoryStream(pe))
        {
            result = scanner.Scan(ms, "fixture.exe", corpus, new ScanOptions());
        }

        Assert.Equal(BinaryFormat.Pe, result.Target.Format);
        Assert.Equal(Architecture.X86_64, result.Target.Architecture);

        IdentifiedComponent libpng = Assert.Single(result.Components, c => c.LibraryName == "libpng");
        Assert.Equal("1.6.37", libpng.Version.Exact);
        Assert.All(result.Components, c => Assert.NotEmpty(c.Evidence));
        Assert.NotEmpty(result.UnidentifiedRegions);
    }

    [Fact]
    public void Scans_macho_and_identifies_seed_library_with_evidence()
    {
        byte[] macho = TestBinaries.MakeMachOWithStrings(
        [
            "OpenSSL 1.1.1k  25 Mar 2021",
            "some vendor-specific blob that matches nothing",
        ]);

        ICorpus corpus = SqliteCorpus.Load(_corpusDb);
        var scanner = new StrataScanner();

        ScanResult result;
        using (var ms = new MemoryStream(macho))
        {
            result = scanner.Scan(ms, "fixture.dylib", corpus, new ScanOptions());
        }

        Assert.Equal(BinaryFormat.MachO, result.Target.Format);
        Assert.Equal(Architecture.X86_64, result.Target.Architecture);

        IdentifiedComponent openssl = Assert.Single(result.Components, c => c.LibraryName == "openssl");
        Assert.Equal("1.1.1k", openssl.Version.Exact);
        Assert.All(result.Components, c => Assert.NotEmpty(c.Evidence));
        Assert.NotEmpty(result.UnidentifiedRegions);
    }

    [Fact]
    public void Scans_pe_with_both_libraries_and_identifies_each()
    {
        byte[] pe = TestBinaries.MakePeWithStrings(
        [
            "libpng version 1.6.37 - April 14, 2019",
            "OpenSSL 1.1.1k  25 Mar 2021",
        ]);

        ICorpus corpus = SqliteCorpus.Load(_corpusDb);
        var scanner = new StrataScanner();

        ScanResult result;
        using (var ms = new MemoryStream(pe))
        {
            result = scanner.Scan(ms, "fixture2.exe", corpus, new ScanOptions());
        }

        Assert.Contains(result.Components, c => c.LibraryName == "libpng");
        Assert.Contains(result.Components, c => c.LibraryName == "openssl");
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
