using System;
using System.Collections.Generic;
using System.IO;
using Strata.Core.Corpus;
using Strata.Core.Fingerprinting;
using Strata.Core.Ingestion;
using Strata.Core.Matching;
using Strata.Core.Model;
using Strata.Core.Recovery;

namespace Strata.Core;

/// <summary>
/// Default <see cref="IScanner"/> — the full pipeline: ingest → recover functions → fingerprint →
/// match (string + function signals fused), streaming progress for the web demo (FR-024). Deterministic:
/// the result is a pure function of (bytes, corpus, options) (Principle IV). When the architecture has no
/// decoder (e.g. AArch64 before Capstone lands), it degrades cleanly to the string signal.
/// </summary>
public sealed class StrataScanner : IScanner
{
    private readonly IBinaryLoader _loader;
    private readonly IFunctionRecovery _recovery;
    private readonly IFingerprinter _fingerprinter;
    private readonly CompositeMatcher _matcher;

    public StrataScanner(
        IBinaryLoader? loader = null,
        IFunctionRecovery? recovery = null,
        IFingerprinter? fingerprinter = null)
    {
        _loader = loader ?? new BinaryLoader();
        _recovery = recovery ?? new FunctionRecovery();
        _fingerprinter = fingerprinter ?? new Fingerprinter();
        _matcher = new CompositeMatcher();
    }

    public ScanResult Scan(
        Stream binary, string name, ICorpus corpus, ScanOptions options, IProgress<ScanProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(binary);
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(options);

        if (corpus.Manifest.SchemaVersion != StrataInfo.SupportedCorpusSchemaVersion)
        {
            throw new Errors.CorpusSchemaMismatchException(
                corpus.Manifest.SchemaVersion, StrataInfo.SupportedCorpusSchemaVersion);
        }

        progress?.Report(new ScanProgress("ingest", $"Parsing {name}", 0.1));
        ScanTarget target = _loader.Load(binary, name, options.Load);

        var warnings = new List<string>();
        if (target.PackingStatus != PackingStatus.NotPacked)
        {
            warnings.Add(
                $"Binary appears {target.PackingStatus.ToString().ToLowerInvariant()} (FR-005): results are not authoritative; Strata does not unpack.");
        }

        progress?.Report(new ScanProgress("recover", "Recovering functions", 0.35));
        IReadOnlyList<RecoveredFunction> functions = _recovery.Recover(target, options.Recovery);

        progress?.Report(new ScanProgress("fingerprint", $"Fingerprinting {functions.Count} functions", 0.55));
        var targetFunctions = new List<TargetFunction>(functions.Count);
        foreach (RecoveredFunction fn in functions)
        {
            targetFunctions.Add(new TargetFunction(fn, _fingerprinter.Fingerprint(fn, target)));
        }

        progress?.Report(new ScanProgress("match", "Matching against corpus", 0.8));
        ScanResult result = _matcher.Match(target, targetFunctions, corpus, options.Match, StrataInfo.Version);

        progress?.Report(new ScanProgress("done", $"{result.Components.Count} component(s) identified", 1.0));
        return warnings.Count == 0 ? result : result with { Warnings = warnings };
    }
}
