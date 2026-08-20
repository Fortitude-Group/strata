using System.Collections.Generic;
using Strata.Core.Corpus;
using Strata.Core.Model;
using Strata.Core.Util;
using Strata.Core.Versioning;
using Xunit;

namespace Strata.Core.Tests;

public sealed class VersionResolverTests
{
    [Fact]
    public void Version_order_compares_numerically_not_lexically()
    {
        Assert.True(VersionOrder.Compare("1.2.11", "1.2.9") > 0);   // 11 > 9, not "11" < "9"
        Assert.True(VersionOrder.Compare("1.1.1k", "1.1.1a") > 0);  // suffix ordering
        Assert.Equal(0, VersionOrder.Compare("1.2.0", "1.2.0"));
    }

    private static CorpusFunctionSignature Fn(string name, string low, string high) => new()
    {
        LibraryName = "lib",
        FunctionName = name,
        CfgShapeHash = 0,
        NormInsnMinHash = [],
        VersionLow = low,
        VersionHigh = high,
    };

    private static readonly IReadOnlyList<EvidenceRecord> SomeEvidence =
    [
        new EvidenceRecord { Kind = EvidenceKind.MatchedFunction, Signal = SignalKind.NormInsn, Detail = "fn" },
    ];

    [Fact]
    public void Present_functions_intersect_to_the_tightest_honest_range()
    {
        // f1 exists in 1.0.0–1.5.0, f2 in 1.2.0–2.0.0 => version is in the intersection 1.2.0–1.5.0 (US2, FR-013).
        var matched = new List<CorpusFunctionSignature> { Fn("f1", "1.0.0", "1.5.0"), Fn("f2", "1.2.0", "2.0.0") };
        VersionResolution v = VersionResolver.FromFunctions(matched, SomeEvidence);

        Assert.Equal(VersionKind.Range, v.Kind);
        Assert.Equal("1.2.0", v.RangeLow);
        Assert.Equal("1.5.0", v.RangeHigh);
    }

    [Fact]
    public void Exact_version_banner_wins_outright()
    {
        var matched = new List<CorpusFunctionSignature>
        {
            Fn("f1", "1.0.0", "2.0.0") with { ExactVersion = "1.4.2" },
        };
        VersionResolution v = VersionResolver.FromFunctions(matched, SomeEvidence);

        Assert.Equal(VersionKind.Exact, v.Kind);
        Assert.Equal("1.4.2", v.Exact);
    }
}
