using System;
using System.Collections.Generic;
using System.Linq;
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

    public sealed record Extracted(
        IReadOnlyList<CorpusStringSignature> Strings,
        IReadOnlyList<CorpusFunctionSignature> Functions,
        Architecture Architecture);

    public static Extracted Extract(
        ScanTarget target, string libraryName, string? purl, string? knownLicense, string? version)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(libraryName);

        List<CorpusStringSignature> stringSigs = target.Strings
            .Select(s => s.Value)
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
            FunctionSignature sig = Fingerprinter.Fingerprint(fn, target);
            functionSigs.Add(new CorpusFunctionSignature
            {
                LibraryName = libraryName,
                FunctionName = $"{libraryName}+0x{fn.StartAddress:x}",
                CfgShapeHash = sig.CfgShapeHash,
                NormInsnMinHash = sig.NormInsnMinHash,
                Distinctiveness = 1.0,
                ExactVersion = version,
            });
        }

        return new Extracted(stringSigs, functionSigs, target.Architecture);
    }
}
