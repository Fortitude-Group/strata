using Strata.Core.Corpus;

namespace Strata.Corpus;

/// <summary>
/// A small, curated seed corpus of distinctive library strings so the US1 MVP is testable end-to-end
/// (task T021). This is NOT the full reproducible corpus (that is the Phase-5 build farm, T059–T063) —
/// it is a hand-picked slice covering a few well-known libraries with version-bearing evidence.
/// </summary>
public static class SeedCorpus
{
    public const string Version = "seed-0.1.0";

    public static IReadOnlyList<CorpusStringSignature> Signatures { get; } =
    [
        // zlib — the copyright banners embed the exact version.
        new()
        {
            LibraryName = "zlib",
            Purl = "pkg:generic/zlib",
            KnownLicense = "Zlib",
            Value = "deflate 1.2.11 Copyright 1995-2017 Jean-loup Gailly and Mark Adler ",
            Distinctiveness = 1.0,
            ExactVersion = "1.2.11",
        },
        new()
        {
            LibraryName = "zlib",
            Purl = "pkg:generic/zlib",
            KnownLicense = "Zlib",
            Value = "inflate 1.2.11 Copyright 1995-2017 Mark Adler ",
            Distinctiveness = 1.0,
            ExactVersion = "1.2.11",
        },
        new()
        {
            LibraryName = "zlib",
            Purl = "pkg:generic/zlib",
            KnownLicense = "Zlib",
            Value = "need dictionary",
            Distinctiveness = 0.35,
            VersionLow = "1.2.0",
            VersionHigh = "1.3.1",
        },

        // SQLite — header magic (range) plus a version-bearing string (exact).
        new()
        {
            LibraryName = "sqlite",
            Purl = "pkg:generic/sqlite",
            KnownLicense = "blessing",
            Value = "SQLite format 3",
            Distinctiveness = 0.8,
            VersionLow = "3.0.0",
            VersionHigh = "3.46.0",
        },
        new()
        {
            LibraryName = "sqlite",
            Purl = "pkg:generic/sqlite",
            KnownLicense = "blessing",
            Value = "3.39.4",
            Distinctiveness = 0.5,
            ExactVersion = "3.39.4",
        },

        // libpng — version banner (exact).
        new()
        {
            LibraryName = "libpng",
            Purl = "pkg:generic/libpng",
            KnownLicense = "Libpng",
            Value = "libpng version 1.6.37 - April 14, 2019",
            Distinctiveness = 1.0,
            ExactVersion = "1.6.37",
        },

        // OpenSSL — version banner (exact).
        new()
        {
            LibraryName = "openssl",
            Purl = "pkg:generic/openssl",
            KnownLicense = "Apache-2.0",
            Value = "OpenSSL 1.1.1k  25 Mar 2021",
            Distinctiveness = 1.0,
            ExactVersion = "1.1.1k",
        },
    ];

    /// <summary>Materialise the seed corpus into a SQLite file (task T021).</summary>
    public static void BuildDatabase(string dbPath) =>
        CorpusWriter.Write(dbPath, Version, Signatures);

    /// <summary>In-memory seed corpus, for tests and the web demo.</summary>
    public static ICorpus AsCorpus() => new InMemoryCorpus(
        new CorpusManifest
        {
            CorpusVersion = Version,
            SchemaVersion = CorpusSchema.SchemaVersion,
            LibraryCount = 4,
            ModelVersion = "parked",
        },
        Signatures);
}
