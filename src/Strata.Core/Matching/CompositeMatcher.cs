using System;
using System.Collections.Generic;
using System.Linq;
using Strata.Core.Corpus;
using Strata.Core.Model;

namespace Strata.Core.Matching;

/// <summary>
/// Fuses the string/constant signal (signal a) with the function-level CFG/instruction signals
/// (signals b/c) into one <see cref="ScanResult"/> (FR-012). Confidence combines independent signals
/// by noisy-OR so agreement across signals strengthens a claim (Principle XII). With function recovery
/// available, unidentified regions are reported at per-function granularity (FR-015/SC-008).
/// </summary>
public sealed class CompositeMatcher
{
    private readonly StringEvidenceMatcher _stringMatcher = new();
    private readonly FunctionEvidenceMatcher _functionMatcher = new();

    public ScanResult Match(
        ScanTarget target,
        IReadOnlyList<TargetFunction> targetFunctions,
        ICorpus corpus,
        MatchOptions options,
        string toolVersion)
    {
        ScanResult stringResult = _stringMatcher.Match(target, corpus, options, toolVersion);
        IReadOnlyList<LibraryFunctionEvidence> functionEvidence = _functionMatcher.Match(targetFunctions, corpus);

        var components = new Dictionary<string, IdentifiedComponent>(StringComparer.Ordinal);
        foreach (IdentifiedComponent c in stringResult.Components)
        {
            components[c.LibraryName] = c;
        }

        foreach (LibraryFunctionEvidence fe in functionEvidence)
        {
            if (components.TryGetValue(fe.LibraryName, out IdentifiedComponent? existing))
            {
                double combined = NoisyOr(existing.Confidence, fe.Coverage);
                var mergedEvidence = existing.Evidence.Concat(fe.Evidence).ToList();
                components[fe.LibraryName] = new IdentifiedComponent(
                    existing.LibraryName, existing.Version, combined, mergedEvidence,
                    existing.Purl, existing.KnownLicense);
            }
            else
            {
                VersionResolution version = ResolveVersionFromFunctions(fe.MatchedCorpusFunctions, fe.Evidence);
                components[fe.LibraryName] = new IdentifiedComponent(
                    fe.LibraryName, version, fe.Coverage, fe.Evidence);
            }
        }

        List<IdentifiedComponent> ordered = components.Values
            .OrderByDescending(c => c.Confidence)
            .ThenBy(c => c.LibraryName, StringComparer.Ordinal)
            .ToList();

        IReadOnlyList<UnidentifiedRegion> unidentified =
            BuildUnidentifiedRegions(targetFunctions, functionEvidence, stringResult);

        return new ScanResult
        {
            Target = target,
            Components = ordered,
            UnidentifiedRegions = unidentified,
            CorpusVersion = corpus.Manifest.CorpusVersion,
            ToolVersion = toolVersion,
        };
    }

    private static double NoisyOr(double a, double b) => Math.Clamp(1.0 - ((1.0 - a) * (1.0 - b)), 0.0, 1.0);

    private static VersionResolution ResolveVersionFromFunctions(
        IReadOnlyList<CorpusFunctionSignature> matched, IReadOnlyList<EvidenceRecord> evidence)
    {
        var exact = matched.Where(m => m.ExactVersion is not null).Select(m => m.ExactVersion!)
            .Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToList();
        if (exact.Count == 1)
        {
            return VersionResolution.OfExact(exact[0], evidence);
        }

        List<string> lows = matched.Where(m => m.VersionLow is not null).Select(m => m.VersionLow!).ToList();
        List<string> highs = matched.Where(m => m.VersionHigh is not null).Select(m => m.VersionHigh!).ToList();
        string low = lows.Count > 0 ? lows.OrderBy(v => v, StringComparer.Ordinal).First()
                    : exact.Count > 0 ? exact.First() : "unknown";
        string high = highs.Count > 0 ? highs.OrderBy(v => v, StringComparer.Ordinal).Last()
                    : exact.Count > 0 ? exact.Last() : "unknown";
        return VersionResolution.OfRange(low, high, evidence);
    }

    private static IReadOnlyList<UnidentifiedRegion> BuildUnidentifiedRegions(
        IReadOnlyList<TargetFunction> targetFunctions,
        IReadOnlyList<LibraryFunctionEvidence> functionEvidence,
        ScanResult stringResult)
    {
        // No function recovery available (e.g. non-x86 target) — fall back to the string matcher's regions.
        if (targetFunctions.Count == 0)
        {
            return stringResult.UnidentifiedRegions;
        }

        var matchedIds = functionEvidence.SelectMany(fe => fe.MatchedTargetFunctionIds).ToHashSet();
        var regions = new List<UnidentifiedRegion>();
        foreach (TargetFunction tf in targetFunctions.OrderBy(t => t.Function.StartAddress))
        {
            if (matchedIds.Contains(tf.Function.Id))
            {
                continue;
            }

            regions.Add(new UnidentifiedRegion
            {
                StartAddress = tf.Function.StartAddress,
                EndAddress = tf.Function.EndAddress,
                Reason = UnidentifiedReason.NoMatch,
                FunctionIds = [tf.Function.Id],
            });
        }

        return regions;
    }
}
