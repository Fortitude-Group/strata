using Strata.Core;
using Strata.Core.Corpus;
using Strata.Core.Fingerprinting;
using Strata.Core.Model;
using Strata.Core.Recovery;

namespace Strata.CorpusBuilder;

/// <summary>
/// Turns one compiled library binary, already resolved to a (library, version), into the corpus
/// signatures it contributes — using the SAME <c>Strata.Core</c> ingestion/recovery/fingerprinting code
/// the scanner runs at scan time (contracts/corpus-builder.md: "run Strata.Core fingerprinting", Principle
/// I). Per-signature distinctiveness (R9, inverse cross-library frequency) is not knowable from a single
/// binary — it is assigned a neutral 1.0 here and recomputed by <see cref="BuildOrchestrator"/> once all
/// binaries in the run have been seen.
/// </summary>
public static class SignatureExtractor
{
    private static readonly FunctionRecovery Recovery = new();
    private static readonly Fingerprinter Fingerprinter = new();

    // Compiler/linker runtime stubs present in EVERY shared object — not library identity. Excluding
    // them from the corpus stops every library from "matching" every target's CRT boilerplate (which
    // otherwise inflates false positives across libraries).
    private static readonly HashSet<string> CrtFunctions = new(StringComparer.Ordinal)
    {
        "_init", "_fini", "_start", "__libc_csu_init", "__libc_csu_fini", "__do_global_dtors_aux",
        "__do_global_ctors_aux", "frame_dummy", "register_tm_clones", "deregister_tm_clones",
        "__gmon_start__", "call_weak_fn", "__stack_chk_fail_local", "atexit", "__cxa_finalize",
        "_dl_relocate_static_pie", "__libc_start_main", "abort",
    };

    private static bool IsCrtFunction(string name) =>
        CrtFunctions.Contains(name) || name.StartsWith("__x86.get_pc_thunk", StringComparison.Ordinal);

    public sealed record Extracted(
        IReadOnlyList<CorpusStringSignature> Strings,
        IReadOnlyList<CorpusFunctionSignature> Functions,
        Architecture Architecture);

    public static Extracted Extract(
        ScanTarget target, string libraryName, string? purl, string? knownLicense, string? version,
        EmbeddingModel? model = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(libraryName);

        Fingerprinter fingerprinter = model is not null ? new Fingerprinter(model) : Fingerprinter;
        var symbolByAddress = new Dictionary<ulong, string>();
        foreach (Symbol s in target.Symbols)
        {
            symbolByAddress.TryAdd(s.Address, s.Name);
        }

        List<CorpusStringSignature> stringSigs = target.Strings
            .Select(s => s.Value)
            .Where(v => !Strata.Core.Ingestion.StringNoise.IsMetadata(v))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .Select(v => new CorpusStringSignature
            {
                LibraryName = libraryName,
                Value = v,
                Distinctiveness = 1.0,
                Purl = purl,
                KnownLicense = knownLicense,
                ExactVersion = version,
            })
            .ToList();

        IReadOnlyList<RecoveredFunction> functions = Recovery.Recover(target, new RecoveryOptions());
        var functionSigs = new List<CorpusFunctionSignature>(functions.Count);
        foreach (RecoveredFunction fn in functions.OrderBy(f => f.StartAddress))
        {
            string name = symbolByAddress.TryGetValue(fn.StartAddress, out string? sym)
                ? sym
                : $"{libraryName}+0x{fn.StartAddress:x}";
            if (sym is not null && IsCrtFunction(sym))
            {
                continue; // skip compiler/linker runtime stubs
            }

            if (fn.Mnemonics.Count < Strata.Core.Matching.FunctionEvidenceMatcher.MinInstructions)
            {
                continue; // too small to fingerprint reliably (non-distinctive, collision-prone)
            }

            FunctionSignature sig = fingerprinter.Fingerprint(fn, target);
            functionSigs.Add(new CorpusFunctionSignature
            {
                LibraryName = libraryName,
                FunctionName = name,
                CfgShapeHash = sig.CfgShapeHash,
                NormInsnMinHash = sig.NormInsnMinHash,
                Embedding = sig.Embedding,
                Distinctiveness = 1.0,
                ExactVersion = version,
            });
        }

        return new Extracted(stringSigs, functionSigs, target.Architecture);
    }
}
