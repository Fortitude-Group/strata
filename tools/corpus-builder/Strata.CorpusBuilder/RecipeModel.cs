namespace Strata.CorpusBuilder;

/// <summary>One version entry in a recipe (contracts/corpus-builder.md version-selection policy).</summary>
public sealed record RecipeVersionModel
{
    public required string Version { get; init; }

    /// <summary>
    /// True when this version is included because it carries a known CVE, not merely because it is the
    /// latest release of its minor line. The policy is "latest per minor line + CVE-flagged versions"
    /// (contracts/corpus-builder.md, research.md R11) — this flag records which rule pulled it in.
    /// </summary>
    public bool CveFlagged { get; init; }
}

/// <summary>
/// A library build recipe (contracts/corpus-builder.md, FR-009/010/011). Parsed from `recipes/*.json`.
/// Drives the containerised build matrix {gcc, clang} × {-O0,-O2,-O3,-Os} × {x86_64, aarch64} authored
/// in `tools/corpus-builder/dockerfiles/`. This offline harness does not itself invoke Docker; it
/// ingests binaries already produced by that matrix and identifies them against these recipes.
/// </summary>
public sealed record RecipeModel
{
    public required string Name { get; init; }

    public string? Purl { get; init; }

    public string? KnownLicense { get; init; }

    public string? SourceUrl { get; init; }

    /// <summary>SHA-256 of the pinned source tarball/tag, for provenance (not verified offline here).</summary>
    public string? Sha256 { get; init; }

    public IReadOnlyList<RecipeVersionModel> Versions { get; init; } = [];

    public IReadOnlyList<string> BuildFlags { get; init; } = [];
}
