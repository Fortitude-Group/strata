using Strata.Core.Corpus;
using Strata.Core.Model;

namespace Strata.Core;

/// <summary>Options controlling ingestion.</summary>
public sealed record LoadOptions
{
    /// <summary>Reject inputs larger than this (OutOfEnvelopeException). 0 = no limit.</summary>
    public long MaxInputBytes { get; init; }

    /// <summary>Minimum length of an ASCII run to count as a recovered string.</summary>
    public int MinStringLength { get; init; } = 5;
}

/// <summary>Options controlling function recovery (research.md R4).</summary>
public sealed record RecoveryOptions
{
    public enum RecoveryPlugin { None, Ghidra, Radare2 }

    public RecoveryPlugin Plugin { get; init; } = RecoveryPlugin.None;
}

/// <summary>Options controlling matching and confidence scoring (research.md R9).</summary>
public sealed record MatchOptions
{
    /// <summary>Components scoring below this are reported as low-confidence rather than omitted (FR-015).
    /// 0.25 balances catching genuine cross-compiler matches (~0.3–0.4 coverage) against tiny-shared-function
    /// noise (~0.01–0.09), which sits an order of magnitude lower.</summary>
    public double MinConfidence { get; init; } = 0.25;
}

/// <summary>Options for a full scan.</summary>
public sealed record ScanOptions
{
    public LoadOptions Load { get; init; } = new();

    public RecoveryOptions Recovery { get; init; } = new();

    public MatchOptions Match { get; init; } = new();

    /// <summary>Optional path to a learned-embedding ONNX model (research.md R7); null = heuristics only.</summary>
    public string? ModelPath { get; init; }
}

/// <summary>Ingestion: parse a native binary into a <see cref="ScanTarget"/> (FR-001..005).</summary>
public interface IBinaryLoader
{
    /// <exception cref="Errors.UnsupportedFormatException">Input is not a supported binary format.</exception>
    /// <exception cref="Errors.OutOfEnvelopeException">Input exceeds the configured size envelope.</exception>
    ScanTarget Load(Stream binary, string name, LoadOptions options);
}

/// <summary>Function-boundary recovery (FR-006).</summary>
public interface IFunctionRecovery
{
    IReadOnlyList<RecoveredFunction> Recover(ScanTarget target, RecoveryOptions options);
}

/// <summary>Per-function fingerprinting (FR-007). Same code builds the corpus (Principle I).</summary>
public interface IFingerprinter
{
    FunctionSignature Fingerprint(RecoveredFunction function, ScanTarget target);
}

/// <summary>Matching + version + confidence — produces the <see cref="ScanResult"/> (FR-012/013/014).</summary>
public interface IMatcher
{
    ScanResult Match(ScanTarget target, ICorpus corpus, MatchOptions options, string toolVersion);
}

/// <summary>The whole pipeline, streaming progress for the web demo (FR-024).</summary>
public interface IScanner
{
    ScanResult Scan(Stream binary, string name, ICorpus corpus, ScanOptions options, IProgress<ScanProgress>? progress = null);
}
