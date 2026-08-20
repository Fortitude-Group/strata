using System.Globalization;
using System.Text.Json;
using Strata.Benchmark;

const int ExitSuccess = 0;
const int ExitError = 1;
const int ExitCheckpointFailed = 2;
const int ExitUsage = 3;

var reportJsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
};

ArgMap parsed = ArgMap.Parse(args, 0);

string? corpusPath = parsed.Get("corpus");
string? binariesDir = parsed.Get("binaries");
string? groundTruthPath = parsed.Get("ground-truth");
string checkpoint = parsed.Get("checkpoint") ?? "A";
string? reportPath = parsed.Get("report");

if (parsed.Has("help") || parsed.Has("h"))
{
    PrintUsage();
    return ExitSuccess;
}

if (corpusPath is null || binariesDir is null || groundTruthPath is null)
{
    Console.Error.WriteLine(
        "error: requires --corpus <db> --binaries <dir> --ground-truth <json> [--checkpoint A|B] [--report <path>]");
    PrintUsage();
    return ExitUsage;
}

if (checkpoint is not ("A" or "a" or "B" or "b"))
{
    Console.Error.WriteLine($"error: --checkpoint must be 'A' or 'B', got '{checkpoint}'");
    return ExitUsage;
}

try
{
    BenchmarkReport report = BenchmarkRunner.Run(corpusPath, binariesDir, groundTruthPath, checkpoint, parsed.Get("model"));

    Console.Out.WriteLine($"corpus version              : {report.CorpusVersion}");
    Console.Out.WriteLine($"binaries evaluated           : {report.BinaryCount}");
    Console.Out.WriteLine($"aggregate precision          : {Pct(report.AggregatePrecision)}");
    Console.Out.WriteLine($"aggregate recall             : {Pct(report.AggregateRecall)}");
    Console.Out.WriteLine($"version-resolution accuracy  : {Pct(report.VersionResolutionAccuracy)}");
    Console.Out.WriteLine(
        $"total wall time              : {report.TotalWallTimeMilliseconds.ToString("F1", CultureInfo.InvariantCulture)} ms");
    Console.Out.WriteLine();
    Console.Out.WriteLine("per-library:");
    foreach (LibraryMetrics m in report.PerLibrary)
    {
        Console.Out.WriteLine(
            $"  {m.Library,-16} precision={Pct(m.Precision)} recall={Pct(m.Recall)} "
            + $"(tp={m.TruePositives} fp={m.FalsePositives} fn={m.FalseNegatives})");
    }

    Console.Out.WriteLine();
    CheckpointResult v = report.CheckpointVerdict;
    string thresholds = v.VersionAccuracyThreshold is { } vt
        ? $"precision>={Pct(v.PrecisionThreshold)} recall>={Pct(v.RecallThreshold)} version>={Pct(vt)}"
        : $"precision>={Pct(v.PrecisionThreshold)} recall>={Pct(v.RecallThreshold)}";
    Console.Out.WriteLine($"checkpoint {v.Checkpoint} ({thresholds}): {(v.Pass ? "PASS" : "FAIL")}");

    if (reportPath is not null)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // SC-010: the report is always written, whether the checkpoint passed or failed.
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, reportJsonOptions));
        Console.Out.WriteLine($"report written to {reportPath}");
    }

    return v.Pass ? ExitSuccess : ExitCheckpointFailed;
}
catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException
    or Strata.Core.Errors.UnsupportedFormatException or Strata.Core.Errors.CorpusSchemaMismatchException
    or Strata.Core.Errors.OutOfEnvelopeException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return ExitError;
}

static string Pct(double v) => (v * 100).ToString("F1", CultureInfo.InvariantCulture) + "%";

void PrintUsage()
{
    Console.Out.WriteLine("strata-benchmark — held-out precision/recall/version-accuracy harness (contracts/corpus-builder.md)");
    Console.Out.WriteLine();
    Console.Out.WriteLine("Usage:");
    Console.Out.WriteLine("  benchmark --corpus <db> --binaries <dir> --ground-truth <json> [--checkpoint A|B] [--report <path>]");
    Console.Out.WriteLine();
    Console.Out.WriteLine("Exit codes: 0 checkpoint pass · 1 error · 2 checkpoint fail · 3 usage");
}
