using Strata.Core;
using Strata.Core.Corpus;
using Strata.Core.Errors;
using Strata.Core.Model;
using Strata.Corpus;
using Strata.Sbom;
using Strata.Sbom.Reports;

namespace Strata.Cli;

/// <summary>Implements `strata scan` (contracts/cli.md, FR-020).</summary>
public static class ScanCommand
{
    public static int Run(ArgMap args, TextWriter stdout, TextWriter stderr)
    {
        string? binaryPath = args.Positional;
        if (string.IsNullOrEmpty(binaryPath))
        {
            stderr.WriteLine("error: 'scan' requires a binary path. Usage: strata scan <binary> [options]");
            return ExitCodes.Usage;
        }

        if (!File.Exists(binaryPath))
        {
            stderr.WriteLine($"error: file not found: {binaryPath}");
            return ExitCodes.Error;
        }

        try
        {
            ICorpus corpus = ResolveCorpus(args.Get("corpus"));

            // Default input cap (512 MiB) on the untrusted-binary path; override with --max-bytes (0 = unlimited).
            long maxBytes = (long)args.GetDouble("max-bytes", 512L * 1024 * 1024);
            var options = new ScanOptions
            {
                Load = new LoadOptions { MaxInputBytes = maxBytes },
                Match = new MatchOptions { MinConfidence = args.GetDouble("min-confidence", 0.25) },
                // Use an explicit --model, else the model bundled alongside the corpus (self-contained
                // artefact), else none. The corpus builder writes model.onnx next to corpus.db.
                ModelPath = args.Get("model") ?? SiblingModel(args.Get("corpus")),
            };

            var log = args.Has("verbose")
                ? new Strata.Core.Diagnostics.StructuredLog(stderr)
                : Strata.Core.Diagnostics.StructuredLog.Null;
            var scanner = new StrataScanner(log: log);
            ScanResult result;
            using (FileStream fs = File.OpenRead(binaryPath))
            {
                result = scanner.Scan(fs, Path.GetFileName(binaryPath), corpus, options);
            }

            // Thin CVE cross-reference (FR-019), on by default; --vuln off disables it.
            if (args.Get("vuln") != "off")
            {
                var xref = new Strata.Vuln.VulnerabilityCrossReference();
                result = result with { Components = xref.Enrich(result.Components) };
            }

            EmitSboms(result, args, stdout);
            EmitReport(result, args, options.Match.MinConfidence, stdout);

            return DetermineExitCode(result);
        }
        catch (UnsupportedFormatException ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return ExitCodes.Error;
        }
        catch (CorpusSchemaMismatchException ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return ExitCodes.Error;
        }
        catch (OutOfEnvelopeException ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return ExitCodes.Error;
        }
    }

    private static ICorpus ResolveCorpus(string? corpusPath) =>
        string.IsNullOrEmpty(corpusPath) ? SeedCorpus.AsCorpus() : SqliteCorpus.Load(corpusPath);

    /// <summary>A `model.onnx` sitting next to the corpus DB, if present, so the artefact is self-contained.</summary>
    private static string? SiblingModel(string? corpusPath)
    {
        if (string.IsNullOrEmpty(corpusPath))
        {
            return null;
        }

        string? dir = Path.GetDirectoryName(Path.GetFullPath(corpusPath));
        string candidate = Path.Combine(dir ?? ".", "model.onnx");
        return File.Exists(candidate) ? candidate : null;
    }

    private static void EmitSboms(ScanResult result, ArgMap args, TextWriter stdout)
    {
        string format = args.Get("format") ?? "cyclonedx";
        string? outPath = args.Get("output");
        var sbomOptions = new SbomOutputOptions { Deterministic = args.Has("deterministic") };

        var formats = new List<SbomFormat>();
        if (format is "cyclonedx" or "both")
        {
            formats.Add(SbomFormat.CycloneDx);
        }

        if (format is "spdx" or "both")
        {
            formats.Add(SbomFormat.Spdx);
        }

        foreach (SbomFormat f in formats)
        {
            string doc = SbomWriter.Emit(result, f, sbomOptions);
            if (outPath is null)
            {
                stdout.WriteLine(doc);
            }
            else
            {
                string ext = f == SbomFormat.CycloneDx ? "cdx.json" : "spdx.json";
                string path = formats.Count > 1 ? $"{outPath}.{ext}" : outPath;
                File.WriteAllText(path, doc);
                stdout.WriteLine($"wrote {f} SBOM -> {path}");
            }
        }
    }

    private static void EmitReport(ScanResult result, ArgMap args, double minConfidence, TextWriter stdout)
    {
        string report = args.Get("report") ?? "text";
        switch (report)
        {
            case "text":
                stdout.WriteLine(TextReport.Render(result, minConfidence));
                break;
            case "json":
                stdout.WriteLine(JsonReport.Render(result));
                break;
            case "none":
            default:
                break;
        }
    }

    private static int DetermineExitCode(ScanResult result)
    {
        if (result.Target.PackingStatus != PackingStatus.NotPacked)
        {
            return ExitCodes.FindingsNeedAttention;
        }

        foreach (IdentifiedComponent c in result.Components)
        {
            if (c.Vulnerabilities.Count > 0)
            {
                return ExitCodes.FindingsNeedAttention;
            }
        }

        return ExitCodes.Success;
    }
}
