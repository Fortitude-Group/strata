using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Strata.Core;
using Strata.Core.Corpus;
using Strata.Core.Model;
using Strata.Corpus;

namespace Strata.Benchmark;

/// <summary>
/// Scans every binary in a held-out ground-truth set against a corpus with <see cref="StrataScanner"/>
/// (the same production pipeline, Principle I) and computes precision, recall, version-resolution
/// accuracy and wall-time (contracts/corpus-builder.md FR-022/023, SC-001/002/003/005).
/// </summary>
public static class BenchmarkRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static BenchmarkReport Run(string corpusPath, string binariesDir, string groundTruthPath, string checkpoint)
    {
        ArgumentException.ThrowIfNullOrEmpty(corpusPath);
        ArgumentException.ThrowIfNullOrEmpty(binariesDir);
        ArgumentException.ThrowIfNullOrEmpty(groundTruthPath);

        ICorpus corpus = SqliteCorpus.Load(corpusPath);
        Dictionary<string, List<GroundTruthComponent>> groundTruth = LoadGroundTruth(groundTruthPath);

        var scanner = new StrataScanner();
        var perBinary = new List<BinaryResult>();
        var perLibraryAgg = new Dictionary<string, (int Tp, int Fp, int Fn)>(StringComparer.OrdinalIgnoreCase);

        int totalTp = 0;
        int totalFp = 0;
        int totalFn = 0;
        int versionMatched = 0;
        int versionCorrect = 0;
        double totalMs = 0;

        foreach ((string fileName, List<GroundTruthComponent> expected) in
            groundTruth.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            Dictionary<string, string> expectedByLib =
                expected.ToDictionary(e => e.Library, e => e.Version, StringComparer.OrdinalIgnoreCase);
            HashSet<string> expectedLibs = new(expectedByLib.Keys, StringComparer.OrdinalIgnoreCase);

            string path = Path.Combine(binariesDir, fileName);
            if (!File.Exists(path))
            {
                foreach (string lib in expectedLibs)
                {
                    Bump(perLibraryAgg, lib, fn: 1);
                }

                totalFn += expectedLibs.Count;
                perBinary.Add(new BinaryResult
                {
                    FileName = fileName,
                    ExpectedLibraries = expectedLibs.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    PredictedLibraries = [],
                    ElapsedMilliseconds = 0,
                    Error = "binary not found",
                });
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            ScanResult result;
            using (FileStream fs = File.OpenRead(path))
            {
                result = scanner.Scan(fs, fileName, corpus, new ScanOptions());
            }

            stopwatch.Stop();
            totalMs += stopwatch.Elapsed.TotalMilliseconds;

            Dictionary<string, string> predictedByLib = result.Components
                .ToDictionary(c => c.LibraryName, c => c.Version.Display, StringComparer.OrdinalIgnoreCase);
            HashSet<string> predictedLibs = new(predictedByLib.Keys, StringComparer.OrdinalIgnoreCase);

            foreach (string lib in predictedLibs)
            {
                if (expectedLibs.Contains(lib))
                {
                    Bump(perLibraryAgg, lib, tp: 1);
                    totalTp++;
                }
                else
                {
                    Bump(perLibraryAgg, lib, fp: 1);
                    totalFp++;
                }
            }

            foreach (string lib in expectedLibs)
            {
                if (!predictedLibs.Contains(lib))
                {
                    Bump(perLibraryAgg, lib, fn: 1);
                    totalFn++;
                }
            }

            foreach (string lib in expectedLibs.Intersect(predictedLibs, StringComparer.OrdinalIgnoreCase))
            {
                versionMatched++;
                if (MinorVersionMatches(expectedByLib[lib], predictedByLib[lib]))
                {
                    versionCorrect++;
                }
            }

            perBinary.Add(new BinaryResult
            {
                FileName = fileName,
                ExpectedLibraries = expectedLibs.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                PredictedLibraries = predictedLibs.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
            });
        }

        double precision = totalTp + totalFp > 0 ? (double)totalTp / (totalTp + totalFp) : 0.0;
        double recall = totalTp + totalFn > 0 ? (double)totalTp / (totalTp + totalFn) : 0.0;
        double versionAccuracy = versionMatched > 0 ? (double)versionCorrect / versionMatched : 0.0;

        List<LibraryMetrics> perLibrary = perLibraryAgg
            .Select(kv => new LibraryMetrics
            {
                Library = kv.Key,
                TruePositives = kv.Value.Tp,
                FalsePositives = kv.Value.Fp,
                FalseNegatives = kv.Value.Fn,
                Precision = kv.Value.Tp + kv.Value.Fp > 0 ? (double)kv.Value.Tp / (kv.Value.Tp + kv.Value.Fp) : 0.0,
                Recall = kv.Value.Tp + kv.Value.Fn > 0 ? (double)kv.Value.Tp / (kv.Value.Tp + kv.Value.Fn) : 0.0,
            })
            .OrderBy(m => m.Library, StringComparer.Ordinal)
            .ToList();

        CheckpointResult verdict = EvaluateCheckpoint(checkpoint, precision, recall, versionAccuracy);

        return new BenchmarkReport
        {
            Checkpoint = checkpoint.ToUpperInvariant(),
            CorpusVersion = corpus.Manifest.CorpusVersion,
            BinaryCount = groundTruth.Count,
            AggregatePrecision = precision,
            AggregateRecall = recall,
            VersionResolutionAccuracy = versionAccuracy,
            TotalWallTimeMilliseconds = totalMs,
            CheckpointVerdict = verdict,
            PerLibrary = perLibrary,
            PerBinary = perBinary,
        };
    }

    private static void Bump(
        Dictionary<string, (int Tp, int Fp, int Fn)> agg, string lib, int tp = 0, int fp = 0, int fn = 0)
    {
        (int Tp, int Fp, int Fn) current = agg.GetValueOrDefault(lib);
        agg[lib] = (current.Tp + tp, current.Fp + fp, current.Fn + fn);
    }

    private static CheckpointResult EvaluateCheckpoint(
        string checkpoint, double precision, double recall, double versionAccuracy)
    {
        return checkpoint.ToUpperInvariant() switch
        {
            "A" => new CheckpointResult
            {
                Checkpoint = "A",
                PrecisionThreshold = 0.80,
                RecallThreshold = 0.60,
                VersionAccuracyThreshold = null,
                Pass = precision >= 0.80 && recall >= 0.60,
            },
            "B" => new CheckpointResult
            {
                Checkpoint = "B",
                PrecisionThreshold = 0.90,
                RecallThreshold = 0.75,
                VersionAccuracyThreshold = 0.70,
                Pass = precision >= 0.90 && recall >= 0.75 && versionAccuracy >= 0.70,
            },
            _ => throw new ArgumentException($"Unknown checkpoint '{checkpoint}'; expected 'A' or 'B'.", nameof(checkpoint)),
        };
    }

    /// <summary>Correct minor version among matched libs (contracts/corpus-builder.md SC-002/003). A
    /// predicted range (e.g. "1.2.8–1.2.11") is compared by its low bound.</summary>
    private static bool MinorVersionMatches(string expected, string predicted)
    {
        string? e = MinorPrefix(expected);
        string? p = MinorPrefix(predicted);
        return e is not null && p is not null && string.Equals(e, p, StringComparison.Ordinal);
    }

    private static string? MinorPrefix(string version)
    {
        string v = version.Split('–', '-')[0].Trim();
        string[] parts = v.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : null;
    }

    private static Dictionary<string, List<GroundTruthComponent>> LoadGroundTruth(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Ground-truth file not found: {path}", path);
        }

        string json = File.ReadAllText(path);
        Dictionary<string, List<GroundTruthComponent>>? gt =
            JsonSerializer.Deserialize<Dictionary<string, List<GroundTruthComponent>>>(json, JsonOptions);
        return gt ?? throw new InvalidOperationException($"Ground-truth file '{path}' deserialized to null.");
    }
}
