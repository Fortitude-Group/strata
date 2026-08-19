using System.Collections.Generic;
using Strata.Core;
using Strata.Core.Corpus;
using Strata.Core.Matching;
using Strata.Core.Model;
using Xunit;

namespace Strata.Core.Tests;

public sealed class MatcherTests
{
    private static ICorpus BuildCorpus() => new InMemoryCorpus(
        new CorpusManifest { CorpusVersion = "test-1", SchemaVersion = 1, LibraryCount = 2, ModelVersion = "parked" },
        [
            new CorpusStringSignature
            {
                LibraryName = "zlib",
                Purl = "pkg:generic/zlib",
                KnownLicense = "Zlib",
                Value = "deflate 1.2.11 Copyright",
                Distinctiveness = 1.0,
                ExactVersion = "1.2.11",
            },
            new CorpusStringSignature
            {
                LibraryName = "zlib",
                Value = "need dictionary",
                Distinctiveness = 0.3,
                VersionLow = "1.2.0",
                VersionHigh = "1.3.1",
            },
            new CorpusStringSignature
            {
                LibraryName = "sqlite",
                Value = "SQLite format 3",
                Distinctiveness = 0.8,
                VersionLow = "3.0.0",
                VersionHigh = "3.46.0",
            },
        ]);

    private static ScanTarget TargetWith(params string[] strings)
    {
        var literals = new List<StringLiteral>();
        ulong offset = 0;
        foreach (string s in strings)
        {
            literals.Add(new StringLiteral(s, offset));
            offset += (ulong)s.Length + 1;
        }

        return new ScanTarget
        {
            Name = "synthetic",
            SizeBytes = (long)offset,
            Format = BinaryFormat.Elf,
            Architecture = Architecture.X86_64,
            Strings = literals,
        };
    }

    [Fact]
    public void Identifies_library_from_a_distinctive_string_with_exact_version()
    {
        ScanTarget target = TargetWith("deflate 1.2.11 Copyright", "some unrelated string here");
        ScanResult result = new StringEvidenceMatcher().Match(target, BuildCorpus(), new MatchOptions(), "test");

        IdentifiedComponent zlib = Assert.Single(result.Components, c => c.LibraryName == "zlib");
        Assert.Equal(VersionKind.Exact, zlib.Version.Kind);
        Assert.Equal("1.2.11", zlib.Version.Exact);
        Assert.NotEmpty(zlib.Evidence);            // FR-014
        Assert.True(zlib.Confidence > 0);
    }

    [Fact]
    public void Resolves_a_range_when_only_range_bearing_strings_match()
    {
        ScanTarget target = TargetWith("SQLite format 3");
        ScanResult result = new StringEvidenceMatcher().Match(target, BuildCorpus(), new MatchOptions(), "test");

        IdentifiedComponent sqlite = Assert.Single(result.Components, c => c.LibraryName == "sqlite");
        Assert.Equal(VersionKind.Range, sqlite.Version.Kind);   // FR-013 — no false precision
        Assert.Equal("3.0.0", sqlite.Version.RangeLow);
        Assert.Equal("3.46.0", sqlite.Version.RangeHigh);
    }

    [Fact]
    public void Partial_match_lowers_confidence_below_full_match()
    {
        // Matches only the low-distinctiveness zlib string, not the distinctive banner.
        ScanTarget partial = TargetWith("need dictionary");
        ScanTarget full = TargetWith("deflate 1.2.11 Copyright", "need dictionary");

        var matcher = new StringEvidenceMatcher();
        double partialConf = Confidence(matcher.Match(partial, BuildCorpus(), new MatchOptions(), "t"), "zlib");
        double fullConf = Confidence(matcher.Match(full, BuildCorpus(), new MatchOptions(), "t"), "zlib");

        Assert.True(fullConf > partialConf);
    }

    [Fact]
    public void Reports_unidentified_region_when_content_is_unattributed()
    {
        ScanTarget target = TargetWith("totally unknown vendor blob string");
        ScanResult result = new StringEvidenceMatcher().Match(target, BuildCorpus(), new MatchOptions(), "test");

        Assert.Empty(result.Components);
        Assert.NotEmpty(result.UnidentifiedRegions);   // SC-008 — honesty, no silent drop
    }

    private static double Confidence(ScanResult r, string lib)
    {
        foreach (IdentifiedComponent c in r.Components)
        {
            if (c.LibraryName == lib)
            {
                return c.Confidence;
            }
        }

        return 0;
    }
}
