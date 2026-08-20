using Strata.CorpusBuilder;

const int ExitSuccess = 0;
const int ExitError = 1;
const int ExitUsage = 3;

if (args.Length == 0)
{
    PrintUsage();
    return ExitUsage;
}

string verb = args[0];
ArgMap parsed = ArgMap.Parse(args, 1);

switch (verb)
{
    case "build":
        return RunBuild(parsed);

    case "train-export":
        return RunTrainExport(parsed);

    case "verify":
        return RunVerify(parsed);

    case "help":
    case "--help":
    case "-h":
        PrintUsage();
        return ExitSuccess;

    default:
        Console.Error.WriteLine($"error: unknown command '{verb}'");
        PrintUsage();
        return ExitUsage;
}

int RunBuild(ArgMap a)
{
    string? recipes = a.Get("recipes");
    string? binaries = a.Get("binaries");
    string? outDir = a.Get("out");
    if (recipes is null || binaries is null || outDir is null)
    {
        Console.Error.WriteLine("error: 'build' requires --recipes <dir> --binaries <dir> --out <corpusDir>");
        return ExitUsage;
    }

    string corpusVersion = a.Get("corpus-version") ?? "1.0.0";
    string? modelPath = a.Get("model");

    try
    {
        BuildOrchestrator.BuildSummary summary = BuildOrchestrator.Build(recipes, binaries, outDir, corpusVersion, modelPath);

        Console.Out.WriteLine($"binaries processed : {summary.BinariesProcessed}");
        Console.Out.WriteLine($"libraries          : {summary.LibraryCount}");
        Console.Out.WriteLine($"string signatures  : {summary.StringSignatureCount}");
        Console.Out.WriteLine($"function signatures: {summary.FunctionSignatureCount}");
        Console.Out.WriteLine($"corpus version     : {summary.Manifest.CorpusVersion}");
        Console.Out.WriteLine($"schema version     : {summary.Manifest.SchemaVersion}");
        Console.Out.WriteLine($"reproducible hash  : {summary.Manifest.BuildReproducibleHash}");
        Console.Out.WriteLine($"wrote              : {System.IO.Path.Combine(outDir, "corpus.db")}");
        Console.Out.WriteLine($"wrote              : {System.IO.Path.Combine(outDir, "manifest.json")}");

        foreach (string warning in summary.Warnings)
        {
            Console.Error.WriteLine($"warning: {warning}");
        }

        return summary.BinariesProcessed == 0 ? ExitError : ExitSuccess;
    }
    catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException
        or System.IO.DirectoryNotFoundException or ArgumentException)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return ExitError;
    }
}

int RunTrainExport(ArgMap a)
{
    string? binaries = a.Get("binaries");
    string? outPath = a.Get("out");
    if (binaries is null || outPath is null)
    {
        Console.Error.WriteLine("error: 'train-export' requires --binaries <dir> --out <features.json>");
        return ExitUsage;
    }

    try
    {
        int count = TrainExport.Export(binaries, outPath);
        Console.Out.WriteLine($"exported {count} labelled training examples to {outPath}");
        return count == 0 ? ExitError : ExitSuccess;
    }
    catch (Exception ex) when (ex is System.IO.IOException or System.IO.DirectoryNotFoundException or ArgumentException)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return ExitError;
    }
}

int RunVerify(ArgMap a)
{
    string? corpusPath = a.Get("corpus");
    if (corpusPath is null)
    {
        Console.Error.WriteLine("error: 'verify' requires --corpus <db>");
        return ExitUsage;
    }

    try
    {
        BuildOrchestrator.VerifyResult result = BuildOrchestrator.Verify(corpusPath);

        Console.Out.WriteLine($"corpus version     : {result.CorpusVersion}");
        Console.Out.WriteLine($"libraries          : {result.LibraryCount}");
        Console.Out.WriteLine(
            $"schema version     : {result.ActualSchemaVersion} (supported {result.SupportedSchemaVersion}) "
            + (result.SchemaOk ? "OK" : "MISMATCH"));

        if (!result.ManifestFound)
        {
            Console.Out.WriteLine("manifest.json      : not found — cannot verify reproducible hash");
        }
        else
        {
            Console.Out.WriteLine($"expected hash      : {result.ExpectedHash}");
            Console.Out.WriteLine($"recomputed hash    : {result.ActualHash}");
            Console.Out.WriteLine($"hash               : {(result.HashOk ? "PASS" : "FAIL")}");
        }

        bool ok = result.SchemaOk && (!result.ManifestFound || result.HashOk);
        Console.Out.WriteLine($"verify             : {(ok ? "PASS" : "FAIL")}");
        return ok ? ExitSuccess : ExitError;
    }
    catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException
        or Strata.Core.Errors.UnsupportedFormatException or Strata.Core.Errors.CorpusSchemaMismatchException)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return ExitError;
    }
}

void PrintUsage()
{
    Console.Out.WriteLine("strata-corpus-builder — builds the reproducible signature corpus (contracts/corpus-builder.md)");
    Console.Out.WriteLine();
    Console.Out.WriteLine("Usage:");
    Console.Out.WriteLine("  corpus-builder build  --recipes <dir> --binaries <dir> --out <corpusDir> [--corpus-version <v>]");
    Console.Out.WriteLine("  corpus-builder verify --corpus <db>");
    Console.Out.WriteLine();
    Console.Out.WriteLine("Exit codes: 0 success · 1 error · 3 usage");
}
