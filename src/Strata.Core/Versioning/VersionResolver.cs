using System.Collections.Generic;
using System.Linq;
using Strata.Core.Corpus;
using Strata.Core.Model;
using Strata.Core.Util;

namespace Strata.Core.Versioning;

/// <summary>
/// Resolves a library's version from matched corpus function signatures (US2, FR-013). Each present
/// function constrains the version to the range it exists in, so the library version lies in the
/// **intersection** of those ranges — a tighter, still-honest bound than a naive union. An exact
/// version banner wins outright; conflicting evidence widens rather than fabricates precision.
/// </summary>
public static class VersionResolver
{
    public static VersionResolution FromFunctions(
        IReadOnlyList<CorpusFunctionSignature> matched, IReadOnlyList<EvidenceRecord> evidence)
    {
        var exact = matched.Where(m => m.ExactVersion is not null).Select(m => m.ExactVersion!)
            .Distinct(System.StringComparer.Ordinal).ToList();
        if (exact.Count == 1)
        {
            return VersionResolution.OfExact(exact[0], evidence);
        }

        List<(string Low, string High)> ranges = matched
            .Where(m => m.VersionLow is not null && m.VersionHigh is not null)
            .Select(m => (m.VersionLow!, m.VersionHigh!))
            .ToList();

        if (ranges.Count == 0)
        {
            // Only exact-but-conflicting evidence: bound by the observed exacts.
            if (exact.Count > 1)
            {
                exact.Sort(VersionOrder.Compare);
                return VersionResolution.OfRange(exact[0], exact[^1], evidence);
            }

            return VersionResolution.OfRange("unknown", "unknown", evidence);
        }

        // Intersection: the highest lower-bound and the lowest upper-bound across present functions.
        string low = ranges.Select(r => r.Low).Aggregate(VersionOrder.Max);
        string high = ranges.Select(r => r.High).Aggregate(VersionOrder.Min);

        // If the intersection collapses (conflicting evidence), fall back to the enclosing union — honest breadth.
        if (VersionOrder.Compare(low, high) > 0)
        {
            low = ranges.Select(r => r.Low).Aggregate(VersionOrder.Min);
            high = ranges.Select(r => r.High).Aggregate(VersionOrder.Max);
        }

        return low == high
            ? VersionResolution.OfExact(low, evidence)
            : VersionResolution.OfRange(low, high, evidence);
    }
}
