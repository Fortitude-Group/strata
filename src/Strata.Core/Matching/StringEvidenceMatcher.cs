using Strata.Core.Corpus;
using Strata.Core.Model;

namespace Strata.Core.Matching;

/// <summary>
/// The MVP matcher: attributes libraries by the string/constant signal (research.md R5 signal a),
/// scores confidence as distinctiveness-weighted coverage (R9), and bounds versions honestly (R8,
/// FR-013). Function-level CFG/instruction/embedding matching layers onto this same aggregation seam
/// as those signals land (tasks T037–T044).
/// </summary>
public sealed class StringEvidenceMatcher : IMatcher
{
    public ScanResult Match(ScanTarget target, ICorpus corpus, MatchOptions options, string toolVersion)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(options);

        // Distinct string values present in the target, excluding ELF/toolchain metadata noise.
        var targetStrings = target.Strings
            .Select(s => s.Value)
            .Where(v => !Ingestion.StringNoise.IsMetadata(v))
            .ToHashSet(StringComparer.Ordinal);

        // Group corpus signatures by library once.
        var byLibrary = corpus.StringSignatures
            .GroupBy(s => s.LibraryName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var components = new List<IdentifiedComponent>();

        foreach ((string library, List<CorpusStringSignature> sigs) in byLibrary)
        {
            List<CorpusStringSignature> matched = sigs.Where(s => targetStrings.Contains(s.Value)).ToList();
            if (matched.Count == 0)
            {
                continue;
            }

            double totalWeight = sigs.Sum(s => s.Distinctiveness);
            double matchedWeight = matched.Sum(s => s.Distinctiveness);
            double confidence = totalWeight > 0 ? Math.Clamp(matchedWeight / totalWeight, 0.0, 1.0) : 0.0;

            List<EvidenceRecord> evidence = matched
                .OrderByDescending(s => s.Distinctiveness)
                .ThenBy(s => s.Value, StringComparer.Ordinal)
                .Select(s => new EvidenceRecord
                {
                    Kind = s.ExactVersion is not null ? EvidenceKind.VersionString : EvidenceKind.MatchedString,
                    Signal = SignalKind.StringConstant,
                    Detail = $"matched string \"{Truncate(s.Value)}\"",
                    Strength = s.Distinctiveness,
                })
                .ToList();

            VersionResolution version = ResolveVersion(matched, sigs);
            CorpusStringSignature meta = matched[0];

            components.Add(new IdentifiedComponent(
                libraryName: library,
                version: version,
                confidence: confidence,
                evidence: evidence,
                purl: meta.Purl,
                knownLicense: meta.KnownLicense));
        }

        // Deterministic ordering (Principle IV).
        components = components
            .OrderByDescending(c => c.Confidence)
            .ThenBy(c => c.LibraryName, StringComparer.Ordinal)
            .ToList();

        IReadOnlyList<UnidentifiedRegion> unidentified = ComputeUnidentifiedRegions(target, corpus, targetStrings);

        return new ScanResult
        {
            Target = target,
            Components = components,
            UnidentifiedRegions = unidentified,
            CorpusVersion = corpus.Manifest.CorpusVersion,
            ToolVersion = toolVersion,
        };
    }

    /// <summary>
    /// FR-013: exact when one version-specific string matched; a range spanning the matched signatures'
    /// bounds otherwise. Never narrower than the evidence.
    /// </summary>
    private static VersionResolution ResolveVersion(
        List<CorpusStringSignature> matched, List<CorpusStringSignature> allLibSigs)
    {
        List<EvidenceRecord> basis = matched
            .Where(s => s.ExactVersion is not null || s.VersionLow is not null || s.VersionHigh is not null)
            .Select(s => new EvidenceRecord
            {
                Kind = s.ExactVersion is not null ? EvidenceKind.VersionString : EvidenceKind.MatchedString,
                Signal = SignalKind.StringConstant,
                Detail = s.ExactVersion is not null
                    ? $"version-specific string \"{Truncate(s.Value)}\" ⇒ {s.ExactVersion}"
                    : $"string \"{Truncate(s.Value)}\" spans {s.VersionLow}–{s.VersionHigh}",
                Strength = s.Distinctiveness,
            })
            .ToList();

        var exactVersions = matched
            .Where(s => s.ExactVersion is not null)
            .Select(s => s.ExactVersion!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        if (exactVersions.Count == 1)
        {
            return VersionResolution.OfExact(exactVersions[0], basis);
        }

        if (exactVersions.Count > 1)
        {
            return VersionResolution.OfRange(exactVersions[0], exactVersions[^1], basis);
        }

        // No exact evidence — bound by the matched signatures' declared ranges.
        List<string> lows = matched.Where(s => s.VersionLow is not null).Select(s => s.VersionLow!).ToList();
        List<string> highs = matched.Where(s => s.VersionHigh is not null).Select(s => s.VersionHigh!).ToList();

        if (lows.Count > 0 || highs.Count > 0)
        {
            string low = lows.Count > 0 ? lows.OrderBy(v => v, StringComparer.Ordinal).First() : "unknown";
            string high = highs.Count > 0 ? highs.OrderBy(v => v, StringComparer.Ordinal).Last() : "unknown";
            return VersionResolution.OfRange(low, high, basis);
        }

        // Fall back to the library's full known window; honest breadth over false precision.
        List<string> allLows = allLibSigs.Where(s => s.VersionLow is not null).Select(s => s.VersionLow!).ToList();
        List<string> allHighs = allLibSigs.Where(s => s.VersionHigh is not null).Select(s => s.VersionHigh!).ToList();
        string wideLow = allLows.Count > 0 ? allLows.OrderBy(v => v, StringComparer.Ordinal).First() : "unknown";
        string wideHigh = allHighs.Count > 0 ? allHighs.OrderBy(v => v, StringComparer.Ordinal).Last() : "unknown";
        return VersionResolution.OfRange(wideLow, wideHigh, basis);
    }

    /// <summary>
    /// FR-015 / SC-008: report unattributed content rather than hiding it. At MVP (string-signal only)
    /// the granularity is whole-file; precise per-function regions arrive with function recovery (T035).
    /// </summary>
    private static IReadOnlyList<UnidentifiedRegion> ComputeUnidentifiedRegions(
        ScanTarget target, ICorpus corpus, HashSet<string> targetStrings)
    {
        var corpusValues = corpus.StringSignatures.Select(s => s.Value).ToHashSet(StringComparer.Ordinal);
        int unmatched = targetStrings.Count(v => !corpusValues.Contains(v));
        if (unmatched == 0)
        {
            return [];
        }

        return
        [
            new UnidentifiedRegion
            {
                StartAddress = 0,
                EndAddress = (ulong)target.SizeBytes,
                Reason = UnidentifiedReason.NoMatch,
                FunctionIds = [],
            },
        ];
    }

    private static string Truncate(string value) => value.Length <= 60 ? value : value[..57] + "...";
}
