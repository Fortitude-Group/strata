namespace Strata.Cli.Tests;

/// <summary>
/// T076: `strata scan`, driven exactly as CI drives it, must produce the three documented gating exit
/// codes (contracts/cli.md, FR-020) so a pipeline can branch on <c>$?</c> without parsing output.
/// </summary>
public sealed class CiExitCodeTests : IDisposable
{
    private readonly string _dir;
    private readonly string _corpusDb;

    public CiExitCodeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "strata-ci-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _corpusDb = Path.Combine(_dir, "seed.db");
        Strata.Corpus.SeedCorpus.BuildDatabase(_corpusDb);
    }

    [Fact]
    public void Clean_scan_with_vuln_check_disabled_exits_success()
    {
        // --vuln off skips CVE cross-reference entirely, and the fixture carries no packer signature and
        // low entropy, so this is the "everything is fine" pipeline outcome (exit 0).
        byte[] elf = TestBinaries.MakeElfWithStrings(["libpng version 1.6.37 - April 14, 2019"]);
        string bin = Path.Combine(_dir, "clean.elf");
        File.WriteAllBytes(bin, elf);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        ArgMap args = ArgMap.Parse(
            ["scan", bin, "--corpus", _corpusDb, "--report", "none", "--vuln", "off"], 1);
        int code = ScanCommand.Run(args, stdout, stderr);

        Assert.Equal(ExitCodes.Success, code);
        Assert.Equal(0, code);
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void Scan_with_a_known_cve_exits_findings_need_attention()
    {
        // OpenSSL 1.1.1k is within the CVE-2021-3712 affected range in the bundled snapshot, and vuln
        // cross-reference is on by default -> the pipeline must fail this build (exit 2), not pass it.
        byte[] elf = TestBinaries.MakeElfWithStrings(["OpenSSL 1.1.1k  25 Mar 2021"]);
        string bin = Path.Combine(_dir, "vulnerable.elf");
        File.WriteAllBytes(bin, elf);

        var stdout = new StringWriter();
        ArgMap args = ArgMap.Parse(
            ["scan", bin, "--corpus", _corpusDb, "--report", "json"], 1);
        int code = ScanCommand.Run(args, stdout, new StringWriter());

        Assert.Equal(ExitCodes.FindingsNeedAttention, code);
        Assert.Equal(2, code);
        Assert.Contains("CVE-2021-3712", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Scanning_a_non_binary_file_exits_error()
    {
        // Not a recognised ELF/PE/Mach-O -> the pipeline should treat this as a tooling/input error
        // (exit 1), distinct from "findings" (exit 2) and "usage" (exit 3).
        string notBinary = Path.Combine(_dir, "readme.txt");
        File.WriteAllText(notBinary, "definitely not a native binary, just prose");

        var stderr = new StringWriter();
        ArgMap args = ArgMap.Parse(["scan", notBinary, "--corpus", _corpusDb, "--report", "none"], 1);
        int code = ScanCommand.Run(args, new StringWriter(), stderr);

        Assert.Equal(ExitCodes.Error, code);
        Assert.Equal(1, code);
        Assert.NotEmpty(stderr.ToString());
    }

    [Fact]
    public void Missing_binary_path_argument_exits_usage()
    {
        // Rounding out the exit-code contract: a usage error (missing required argument) is its own
        // code (3), distinct from a runtime error against a real file (1).
        ArgMap args = ArgMap.Parse(["scan"], 1);
        int code = ScanCommand.Run(args, new StringWriter(), new StringWriter());

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Equal(3, code);
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
