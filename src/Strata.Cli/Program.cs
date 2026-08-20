using Strata.Cli;
using Strata.Core;
using Strata.Corpus;

if (args.Length == 0)
{
    PrintUsage();
    return ExitCodes.Usage;
}

string verb = args[0];
ArgMap parsed = ArgMap.Parse(args, 1);

switch (verb)
{
    case "scan":
        return ScanCommand.Run(parsed, Console.Out, Console.Error);

    case "version":
        Console.Out.WriteLine($"strata {StrataInfo.Version}");
        Console.Out.WriteLine($"corpus schema (supported): v{StrataInfo.SupportedCorpusSchemaVersion}");
        return ExitCodes.Success;

    case "corpus":
        return CorpusInfo(parsed);

    case "help":
    case "--help":
    case "-h":
        PrintUsage();
        return ExitCodes.Success;

    default:
        Console.Error.WriteLine($"error: unknown command '{verb}'");
        PrintUsage();
        return ExitCodes.Usage;
}

int CorpusInfo(ArgMap a)
{
    string? path = a.Get("corpus");
    Strata.Core.Corpus.ICorpus corpus;
    try
    {
        corpus = path is null ? SeedCorpus.AsCorpus() : SqliteCorpus.Load(path);
    }
    catch (Strata.Core.Errors.StrataException ex)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return ExitCodes.Error;
    }

    Console.Out.WriteLine($"corpus version : {corpus.Manifest.CorpusVersion}");
    Console.Out.WriteLine($"schema version : {corpus.Manifest.SchemaVersion}");
    Console.Out.WriteLine($"libraries      : {corpus.Manifest.LibraryCount}");
    Console.Out.WriteLine($"string sigs    : {corpus.StringSignatures.Count}");
    Console.Out.WriteLine($"function sigs  : {corpus.FunctionSignatures.Count}");
    Console.Out.WriteLine($"model          : {corpus.Manifest.ModelVersion ?? "parked"}");

    if (a.Positional == "verify")
    {
        bool schemaOk = corpus.Manifest.SchemaVersion == StrataInfo.SupportedCorpusSchemaVersion;
        Console.Out.WriteLine(schemaOk
            ? "verify         : OK (schema supported)"
            : $"verify         : FAIL (schema {corpus.Manifest.SchemaVersion} not supported by this build)");
        return schemaOk ? ExitCodes.Success : ExitCodes.Error;
    }

    return ExitCodes.Success;
}

void PrintUsage()
{
    Console.Out.WriteLine("strata — evidence-backed SBOMs from native binaries");
    Console.Out.WriteLine();
    Console.Out.WriteLine("Usage:");
    Console.Out.WriteLine("  strata scan <binary> [--format cyclonedx|spdx|both] [--output <path>]");
    Console.Out.WriteLine("                       [--report text|json|none] [--corpus <path>]");
    Console.Out.WriteLine("                       [--min-confidence <0..1>] [--deterministic]");
    Console.Out.WriteLine("  strata corpus [--corpus <path>]");
    Console.Out.WriteLine("  strata version");
    Console.Out.WriteLine();
    Console.Out.WriteLine("Exit codes: 0 success · 1 error · 2 findings need attention · 3 usage");
}
