using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Strata.Core;
using Strata.Core.Corpus;
using Strata.Core.Ingestion;
using Strata.Core.Model;
using Strata.Corpus;

namespace Strata.CorpusBuilder;

/// <summary>
/// Builds a signature corpus DB + manifest from a set of recipes and a directory of pre-compiled
/// library binaries (contracts/corpus-builder.md, FR-009/010/011). This offline harness has no live
/// Docker toolchain (see `tools/corpus-builder/dockerfiles/`): it consumes binaries already produced by
/// that {gcc, clang} × {-O0,-O2,-O3,-Os} × {x86_64, aarch64} matrix rather than invoking the compilers
/// itself, and identifies each binary against the recipes by filename (`&lt;library&gt;-&lt;version&gt;...`).
/// </summary>
public static class BuildOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    // Declared build-matrix metadata (contracts/signature-db.md manifest.json), matching the pinned
    // images in tools/corpus-builder/dockerfiles/. Not extracted from the binaries — this harness
    // ingests pre-compiled artefacts rather than invoking the compilers itself.
    private static readonly IReadOnlyList<ToolchainInfo> DeclaredToolchains =
    [
        new ToolchainInfo { Compiler = "gcc", Version = "13.2.0" },
        new ToolchainInfo { Compiler = "clang", Version = "17.0.6" },
    ];

    public sealed record BuildSummary(
        int LibraryCount,
        int BinariesProcessed,
        int StringSignatureCount,
        int FunctionSignatureCount,
        IReadOnlyList<string> Warnings,
        CorpusBuildManifest Manifest);

    public sealed record VerifyResult(
        bool SchemaOk,
        bool HashOk,
        int SupportedSchemaVersion,
        int ActualSchemaVersion,
        string ExpectedHash,
        string ActualHash,
        string CorpusVersion,
        int LibraryCount,
        bool ManifestFound);

    public static BuildSummary Build(string recipesDir, string binariesDir, string outDir, string corpusVersion)
    {
        ArgumentException.ThrowIfNullOrEmpty(recipesDir);
        ArgumentException.ThrowIfNullOrEmpty(binariesDir);
        ArgumentException.ThrowIfNullOrEmpty(outDir);
        ArgumentException.ThrowIfNullOrEmpty(corpusVersion);

        List<RecipeModel> recipes = LoadRecipes(recipesDir);
        if (!Directory.Exists(binariesDir))
        {
            throw new DirectoryNotFoundException($"Binaries directory not found: {binariesDir}");
        }

        Directory.CreateDirectory(outDir);

        var warnings = new List<string>();
        var allStrings = new List<CorpusStringSignature>();
        var allFunctions = new List<CorpusFunctionSignature>();
        var librariesBuilt = new SortedSet<string>(StringComparer.Ordinal);
        var archesSeen = new SortedSet<string>(StringComparer.Ordinal);
        int binariesProcessed = 0;

        var loader = new BinaryLoader();
        foreach (string binaryPath in Directory.EnumerateFiles(binariesDir).OrderBy(p => p, StringComparer.Ordinal))
        {
            string fileName = Path.GetFileName(binaryPath);
            (RecipeModel? recipe, RecipeVersionModel? version) = ResolveLibraryVersion(fileName, recipes);
            if (recipe is null)
            {
                warnings.Add($"'{fileName}': no recipe name matched — skipped.");
                continue;
            }

            if (version is null)
            {
                warnings.Add(
                    $"'{fileName}': matched recipe '{recipe.Name}' but no recipe version appears in the filename — skipped.");
                continue;
            }

            ScanTarget target;
            using (FileStream fs = File.OpenRead(binaryPath))
            {
                target = loader.Load(fs, fileName, new LoadOptions());
            }

            SignatureExtractor.Extracted extracted = SignatureExtractor.Extract(
                target, recipe.Name, recipe.Purl, recipe.KnownLicense, version.Version);

            allStrings.AddRange(extracted.Strings);
            allFunctions.AddRange(extracted.Functions);
            librariesBuilt.Add(recipe.Name);
            archesSeen.Add(ArchName(extracted.Architecture));
            binariesProcessed++;
        }

        List<CorpusStringSignature> withDistinctiveness = ApplyStringDistinctiveness(allStrings);
        List<CorpusFunctionSignature> fnWithDistinctiveness = ApplyFunctionDistinctiveness(allFunctions);

        string dbPath = Path.Combine(outDir, "corpus.db");
        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }

        CorpusWriter.Write(dbPath, corpusVersion, withDistinctiveness, fnWithDistinctiveness, modelVersion: null);

        string hash = ComputeReproducibleHash(withDistinctiveness, fnWithDistinctiveness);
        List<string> optLevels = recipes
            .SelectMany(r => r.BuildFlags)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        var manifest = new CorpusBuildManifest
        {
            CorpusVersion = corpusVersion,
            SchemaVersion = CorpusSchema.SchemaVersion,
            Toolchains = DeclaredToolchains,
            OptLevels = optLevels,
            Arches = archesSeen.ToList(),
            LibraryCount = librariesBuilt.Count,
            ModelVersion = "parked",
            BuildReproducibleHash = hash,
        };

        string manifestPath = Path.Combine(outDir, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

        return new BuildSummary(
            librariesBuilt.Count,
            binariesProcessed,
            withDistinctiveness.Count,
            fnWithDistinctiveness.Count,
            warnings,
            manifest);
    }

    public static VerifyResult Verify(string dbPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(dbPath);

        ICorpus corpus = SqliteCorpus.Load(dbPath);
        string actualHash = ComputeReproducibleHash(corpus.StringSignatures, corpus.FunctionSignatures);

        string? dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        string manifestPath = Path.Combine(dir ?? ".", "manifest.json");

        string expectedHash = "(no manifest.json found)";
        bool hashOk = false;
        bool manifestFound = File.Exists(manifestPath);
        if (manifestFound)
        {
            string json = File.ReadAllText(manifestPath);
            CorpusBuildManifest? manifest = JsonSerializer.Deserialize<CorpusBuildManifest>(json, JsonOptions);
            if (manifest is not null)
            {
                expectedHash = manifest.BuildReproducibleHash;
                hashOk = string.Equals(expectedHash, actualHash, StringComparison.Ordinal);
            }
        }

        return new VerifyResult(
            SchemaOk: corpus.Manifest.SchemaVersion == StrataInfo.SupportedCorpusSchemaVersion,
            HashOk: hashOk,
            SupportedSchemaVersion: StrataInfo.SupportedCorpusSchemaVersion,
            ActualSchemaVersion: corpus.Manifest.SchemaVersion,
            ExpectedHash: expectedHash,
            ActualHash: actualHash,
            CorpusVersion: corpus.Manifest.CorpusVersion,
            LibraryCount: corpus.Manifest.LibraryCount,
            ManifestFound: manifestFound);
    }

    /// <summary>
    /// sha256 over the sorted, canonicalised signature content — SC-009. No timestamps or host paths
    /// participate, so re-running the build from the same inputs reproduces the same hash.
    /// </summary>
    private static string ComputeReproducibleHash(
        IReadOnlyList<CorpusStringSignature> strings, IReadOnlyList<CorpusFunctionSignature> functions)
    {
        var sb = new StringBuilder();

        foreach (CorpusStringSignature s in strings
            .OrderBy(x => x.LibraryName, StringComparer.Ordinal)
            .ThenBy(x => x.Value, StringComparer.Ordinal)
            .ThenBy(x => x.ExactVersion, StringComparer.Ordinal))
        {
            sb.Append("S|").Append(s.LibraryName).Append('|').Append(s.Value).Append('|')
              .Append(s.Distinctiveness.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(s.ExactVersion ?? string.Empty).Append('|')
              .Append(s.VersionLow ?? string.Empty).Append('|')
              .Append(s.VersionHigh ?? string.Empty).Append('\n');
        }

        foreach (CorpusFunctionSignature f in functions
            .OrderBy(x => x.LibraryName, StringComparer.Ordinal)
            .ThenBy(x => x.FunctionName, StringComparer.Ordinal))
        {
            sb.Append("F|").Append(f.LibraryName).Append('|').Append(f.FunctionName).Append('|')
              .Append(f.CfgShapeHash.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(string.Join(',', f.NormInsnMinHash)).Append('|')
              .Append(f.Distinctiveness.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(f.ExactVersion ?? string.Empty).Append('|')
              .Append(f.VersionLow ?? string.Empty).Append('|')
              .Append(f.VersionHigh ?? string.Empty).Append('\n');
        }

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        byte[] hash = SHA256.HashData(bytes);
        return "sha256:" + Convert.ToHexStringLower(hash);
    }

    /// <summary>R9: distinctiveness = inverse cross-library frequency of this exact string value.</summary>
    private static List<CorpusStringSignature> ApplyStringDistinctiveness(List<CorpusStringSignature> sigs)
    {
        Dictionary<string, int> libraryCountByValue = sigs
            .GroupBy(s => s.Value, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(s => s.LibraryName).Distinct(StringComparer.Ordinal).Count(),
                StringComparer.Ordinal);

        return sigs
            .Select(s => s with { Distinctiveness = 1.0 / libraryCountByValue[s.Value] })
            .OrderBy(s => s.LibraryName, StringComparer.Ordinal)
            .ThenBy(s => s.Value, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>R9: distinctiveness = inverse cross-library frequency of this CFG-shape hash.</summary>
    private static List<CorpusFunctionSignature> ApplyFunctionDistinctiveness(List<CorpusFunctionSignature> sigs)
    {
        Dictionary<ulong, int> libraryCountByHash = sigs
            .GroupBy(f => f.CfgShapeHash)
            .ToDictionary(
                g => g.Key,
                g => g.Select(f => f.LibraryName).Distinct(StringComparer.Ordinal).Count());

        return sigs
            .Select(f => f with { Distinctiveness = 1.0 / libraryCountByHash[f.CfgShapeHash] })
            .OrderBy(f => f.LibraryName, StringComparer.Ordinal)
            .ThenBy(f => f.FunctionName, StringComparer.Ordinal)
            .ToList();
    }

    private static List<RecipeModel> LoadRecipes(string recipesDir)
    {
        if (!Directory.Exists(recipesDir))
        {
            throw new DirectoryNotFoundException($"Recipes directory not found: {recipesDir}");
        }

        var recipes = new List<RecipeModel>();
        foreach (string path in Directory.EnumerateFiles(recipesDir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            string json = File.ReadAllText(path);
            RecipeModel recipe = JsonSerializer.Deserialize<RecipeModel>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Recipe '{path}' deserialized to null.");
            recipes.Add(recipe);
        }

        if (recipes.Count == 0)
        {
            throw new InvalidOperationException($"No recipe JSON files found under '{recipesDir}'.");
        }

        return recipes;
    }

    private static (RecipeModel? recipe, RecipeVersionModel? version) ResolveLibraryVersion(
        string fileName, IReadOnlyList<RecipeModel> recipes)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);

        RecipeModel? bestRecipe = null;
        foreach (RecipeModel r in recipes)
        {
            if (stem.StartsWith(r.Name, StringComparison.OrdinalIgnoreCase)
                && (bestRecipe is null || r.Name.Length > bestRecipe.Name.Length))
            {
                bestRecipe = r;
            }
        }

        if (bestRecipe is null)
        {
            return (null, null);
        }

        RecipeVersionModel? version = bestRecipe.Versions
            .Where(v => stem.Contains(v.Version, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.Version.Length)
            .FirstOrDefault();

        return (bestRecipe, version);
    }

    private static string ArchName(Architecture arch) => arch switch
    {
        Architecture.X86_64 => "x86_64",
        Architecture.AArch64 => "aarch64",
        Architecture.Other => "other",
        _ => "unknown",
    };
}
