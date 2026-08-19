using System;
using System.Collections.Generic;
using System.IO;
using Strata.Core.Corpus;
using Strata.Core.Ingestion;
using Strata.Core.Matching;
using Strata.Core.Model;

namespace Strata.Core;

/// <summary>
/// Default <see cref="IScanner"/> — the ingestion → match pipeline, streaming progress for the web demo
/// (FR-024). Deterministic: the result is a pure function of (bytes, corpus, options); progress events
/// are UX only and never change it (Principle IV). Function recovery + fingerprinting insert between
/// ingest and match as those signals land (tasks T035–T044); the MVP runs the string signal directly.
/// </summary>
public sealed class StrataScanner : IScanner
{
    private readonly IBinaryLoader _loader;
    private readonly IMatcher _matcher;

    public StrataScanner(IBinaryLoader? loader = null, IMatcher? matcher = null)
    {
        _loader = loader ?? new BinaryLoader();
        _matcher = matcher ?? new StringEvidenceMatcher();
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

        progress?.Report(new ScanProgress("match", "Matching against corpus", 0.6));
        ScanResult result = _matcher.Match(target, corpus, options.Match, StrataInfo.Version);

        progress?.Report(new ScanProgress("done", $"{result.Components.Count} component(s) identified", 1.0));
        return warnings.Count == 0 ? result : result with { Warnings = warnings };
    }
}
