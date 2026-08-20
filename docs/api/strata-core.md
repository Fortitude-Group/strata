# `Strata.Core` API reference

`Strata.Core` is the engine assembly: ingestion → function recovery → fingerprinting → matching →
`ScanResult`. It is the **one** implementation of the pipeline — the CLI, the web demo, the corpus
builder, and the benchmark harness all consume it directly (Principle I, "one engine"). This document
covers the public surface: what each type is for, its key members, and the invariants it upholds.
Namespaces: `Strata.Core`, `Strata.Core.Model`, `Strata.Core.Corpus`, `Strata.Core.Ingestion`,
`Strata.Core.Recovery`, `Strata.Core.Fingerprinting`, `Strata.Core.Matching`, `Strata.Core.Versioning`,
`Strata.Core.Errors`, `Strata.Core.Diagnostics`.

Signatures below are taken directly from the source (`src/Strata.Core/`); the conceptual contract also
lives at `specs/001-strata-binary-sbom/contracts/engine-api.md`.

## Entry point: `IScanner` / `StrataScanner`

```csharp
public interface IScanner
{
    ScanResult Scan(Stream binary, string name, ICorpus corpus, ScanOptions options,
                     IProgress<ScanProgress>? progress = null);
}
```

`StrataScanner` (`Strata.Core.StrataScanner`) is the default, and normally only, implementation. It
composes `IBinaryLoader → IFunctionRecovery → IFingerprinter → CompositeMatcher` and streams five
progress events (`ingest`, `recover`, `fingerprint`, `match`, `done`) via `IProgress<ScanProgress>` —
purely for UX (e.g. the web demo's "libraries light up" reveal); progress never affects the result.

```csharp
public StrataScanner(
    IBinaryLoader? loader = null,
    IFunctionRecovery? recovery = null,
    IFingerprinter? fingerprinter = null,
    Diagnostics.StructuredLog? log = null)
```

All four dependencies default to the first-party implementation (`BinaryLoader`, `FunctionRecovery`,
`Fingerprinter`, `StructuredLog.Null`), so `new StrataScanner()` is a complete, working scanner. The
constructor arguments exist for test seams and for swapping in a learned-embedding-aware
`Fingerprinter` — which `Scan` itself does per-call, loading `options.ModelPath` via
`Fingerprinting.EmbeddingModel.TryLoad` and falling back to the injected default fingerprinter when no
model is present or loadable.

**Invariants:**
- **Determinism** (Principle IV): `Scan` is a pure function of `(binary bytes, corpus, options)`. No
  wall-clock time, RNG, or machine-specific state enters `ScanResult`.
- **Corpus schema gate**: throws `CorpusSchemaMismatchException` up front if
  `corpus.Manifest.SchemaVersion != StrataInfo.SupportedCorpusSchemaVersion` — before any parsing work.
- **Packing is flagged, never unpacked**: if `target.PackingStatus != PackingStatus.NotPacked`, a
  warning is appended to `ScanResult.Warnings`; the scan still runs (Strata does not unpack).
- **Graceful degrade**: when the architecture has no instruction decoder (function recovery yields no
  functions), matching falls back cleanly to the string/constant signal alone.

## Options

All under `Strata.Core`, all immutable `record`s with sensible defaults — `new ScanOptions()` is a
valid, working configuration.

```csharp
public sealed record LoadOptions
{
    public long MaxInputBytes { get; init; }        // 0 = no limit (default)
    public int MinStringLength { get; init; } = 5;
}

public sealed record RecoveryOptions
{
    public enum RecoveryPlugin { None, Ghidra, Radare2 }
    public RecoveryPlugin Plugin { get; init; } = RecoveryPlugin.None;
}

public sealed record MatchOptions
{
    public double MinConfidence { get; init; } = 0.5;
}

public sealed record ScanOptions
{
    public LoadOptions Load { get; init; } = new();
    public RecoveryOptions Recovery { get; init; } = new();
    public MatchOptions Match { get; init; } = new();
    public string? ModelPath { get; init; }          // optional ONNX embedding model; null = heuristics only
}
```

Notes:
- `LoadOptions.MaxInputBytes` defaults to **0, meaning unlimited**. Callers that accept untrusted
  input (the CLI on arbitrary vendor binaries, any server-side integration) should set an explicit
  limit; see `docs/security-review.md` §(b) for why this matters and where the current gap is.
- `RecoveryOptions.Plugin` is a declared seam for an optional Ghidra/radare2 depth plug-in
  (research.md R4); as of this build only `None` (the first-party linear-sweep recovery) is wired.
- `MatchOptions.MinConfidence` is advisory to callers/report renderers (e.g. the CLI's text report
  greys out below-threshold components) — the engine does not itself drop low-confidence components;
  see FR-015 (honesty invariant) in the spec.

## Ingestion — `IBinaryLoader`

```csharp
public interface IBinaryLoader
{
    ScanTarget Load(Stream binary, string name, LoadOptions options);
}
```

- **Throws** `UnsupportedFormatException` when the input isn't a recognised ELF/PE/Mach-O (FR-004:
  "refuse to guess" rather than fabricate a result), and `OutOfEnvelopeException` when the stream
  exceeds `LoadOptions.MaxInputBytes`.
- Default implementation `BinaryLoader` composes `FormatDetector` (magic-byte sniffing) with a
  per-format reader (`ElfReader`, `PeReader`, `MachOReader` — all tolerant/best-effort: a malformed
  section table degrades to header-only facts instead of failing the whole scan), plus
  `StringConstantExtractor` (whole-file printable-ASCII scan) and `PackingDetector` (known packer
  signatures + Shannon entropy ≥ 7.5 ⇒ `PackingStatus.Suspected`).
- Produces a `ScanTarget` — see below.

## `ScanTarget` (`Strata.Core.Model`)

The immutable snapshot of what ingestion extracted; every downstream stage reads it, none mutate it.

```csharp
public sealed record ScanTarget
{
    public required string Name { get; init; }
    public required long SizeBytes { get; init; }
    public required BinaryFormat Format { get; init; }        // Elf | Pe | MachO | Unknown
    public required Architecture Architecture { get; init; }  // X86_64 | AArch64 | Other | Unknown
    public Linkage Linkage { get; init; } = Linkage.Unknown;
    public PackingStatus PackingStatus { get; init; } = PackingStatus.NotPacked;
    public IReadOnlyList<Section> Sections { get; init; } = [];
    public IReadOnlyList<ulong> EntryPoints { get; init; } = [];
    public IReadOnlyList<Symbol> Symbols { get; init; } = [];       // empty for stripped binaries — the common case
    public IReadOnlyList<StringLiteral> Strings { get; init; } = [];
    public IReadOnlyList<ConstantBlob> Constants { get; init; } = [];
    [JsonIgnore] public ReadOnlyMemory<byte> Image { get; init; }   // raw bytes; excluded from JSON/equality
}
```

`Image` is JSON-ignored deliberately — reports and telemetry must never embed the whole binary.

## Function recovery — `IFunctionRecovery`

```csharp
public interface IFunctionRecovery
{
    IReadOnlyList<RecoveredFunction> Recover(ScanTarget target, RecoveryOptions options);
}
```

Default implementation `FunctionRecovery` (`Strata.Core.Recovery`) does a linear-sweep decode of each
executable section (via `Disassembly.IInstructionDecoder` — Iced for x86-64, Capstone for AArch64) and
treats symbol addresses (when present) plus call targets as function-entry candidates, then builds each
function's CFG with `CfgBuilder.Build`. Each `RecoveredFunction` carries a `RecoveryConfidence` (0.6 for
the linear-sweep fast path; symbol-driven boundaries are higher-confidence).

```csharp
public sealed record RecoveredFunction
{
    public required int Id { get; init; }
    public required ulong StartAddress { get; init; }
    public required ulong EndAddress { get; init; }
    public IReadOnlyList<BasicBlock> BasicBlocks { get; init; } = [];
    public IReadOnlyList<(int From, int To)> Edges { get; init; } = [];
    public IReadOnlyList<string> Mnemonics { get; init; } = [];   // operands abstracted away
    public double RecoveryConfidence { get; init; } = 1.0;
    public ulong SizeBytes => EndAddress > StartAddress ? EndAddress - StartAddress : 0;
}
```

`RecoveryOptions` currently has no cap on function/instruction count for a pathologically dense or
degenerate section — see `docs/security-review.md` §(b) for the concrete risk and recommendation.

## Fingerprinting — `IFingerprinter`

```csharp
public interface IFingerprinter
{
    FunctionSignature Fingerprint(RecoveredFunction function, ScanTarget target);
}
```

**The same fingerprinting code builds the corpus and fingerprints scan targets** (Principle I) — a
target function and its corpus counterpart are always compared on identical features. Default
implementation `Fingerprinter` combines three-and-a-half independent signals, each targeting a
different invariance so they fail independently:

```csharp
public sealed record FunctionSignature
{
    public required int FunctionId { get; init; }
    public IReadOnlyCollection<string> StringConstRefs { get; init; } = [];  // (a) reserved; not populated by Fingerprinter
    public ulong CfgShapeHash { get; init; }                                  // (b) CfgShapeSignal
    public IReadOnlyList<uint> NormInsnMinHash { get; init; } = [];           // (c) NormInsnSignal
    public IReadOnlyList<float>? Embedding { get; init; }                     // (d) optional, null when parked
}
```

- **(b) `CfgShapeSignal`** — FNV-1a64 hash of block count, edge count, and the *sorted* in/out-degree
  sequences of the function's CFG. Address- and register-allocation-independent; stable across
  compilers/optimisation levels for the same source.
- **(c) `NormInsnSignal`** — a 32-permutation `MinHash` (`Strata.Core.Fingerprinting.MinHash`) over
  4-gram shingles of normalised mnemonics (operands abstracted). `MinHash.Similarity` estimates
  Jaccard similarity between two signatures for matching.
- **(d) Embedding** — only populated when an `EmbeddingModel` (ONNX Runtime in-process inference over
  an `OpcodeHistogram` feature vector) is supplied to the `Fingerprinter` constructor. `null` when the
  model is absent — the deliberately "parked" state (see CHANGELOG SC-004 decision). A parked model
  never breaks the pipeline; it is simply an absent signal.
- All signals are **pure functions of the function's bytes + architecture** (Principle IV) — same
  input, same fingerprint, forever.

## Matching — `IMatcher` / `CompositeMatcher`

```csharp
public interface IMatcher
{
    ScanResult Match(ScanTarget target, ICorpus corpus, MatchOptions options, string toolVersion);
}
```

The conceptual contract's `IMatcher` is the string-only MVP surface, still implemented literally by
`Matching.StringEvidenceMatcher`. The scanner instead drives `Matching.CompositeMatcher` (not an
`IMatcher` itself, but the same shape plus a target-functions parameter), which fuses:

- **String/constant evidence** (`StringEvidenceMatcher`) — confidence = distinctiveness-weighted
  coverage of a library's corpus strings that appear verbatim in the target.
- **Function evidence** (`FunctionEvidenceMatcher`) — for each recovered target function, finds its
  best corpus match via `Fingerprinting.LshIndex` (banded MinHash-LSH candidate retrieval, `Bands=8`,
  exact CFG-shape hash as a second recall channel) with a similarity floor of `0.7`; when both sides
  carry an embedding, cosine similarity can also promote a candidate. Per-library **coverage** =
  matched corpus functions ÷ `corpus.FunctionCount(library)`.
- **Fusion**: when a library has evidence from both signals, confidence combines by **noisy-OR**
  (`1 - (1-a)(1-b)`) — independent-signal agreement strengthens a claim (Principle XII), never simple
  averaging that could dilute a strong single-signal hit.
- **Version resolution** for function-only matches delegates to `Versioning.VersionResolver`.

## `ScanResult` and its components

```csharp
public sealed record ScanResult
{
    public required ScanTarget Target { get; init; }
    public IReadOnlyList<IdentifiedComponent> Components { get; init; } = [];
    public IReadOnlyList<UnidentifiedRegion> UnidentifiedRegions { get; init; } = [];
    public required string CorpusVersion { get; init; }
    public required string ToolVersion { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
```

`ScanResult` is a pure function of `(target bytes, corpus version, tool version, vuln snapshot)` —
nondeterministic presentation fields (SBOM serial numbers, timestamps) live in the SBOM output layer,
never here. `Components` is always ordered by confidence descending, then library name (deterministic
tie-break).

### `IdentifiedComponent`

```csharp
public sealed record IdentifiedComponent
{
    public IdentifiedComponent(string libraryName, VersionResolution version, double confidence,
        IReadOnlyList<EvidenceRecord> evidence, string? purl = null, string? knownLicense = null,
        IReadOnlyList<VulnerabilityReference>? vulnerabilities = null);

    public string LibraryName { get; }
    public VersionResolution Version { get; }
    public double Confidence { get; }                       // 0..1, distinctiveness-weighted coverage
    public IReadOnlyList<EvidenceRecord> Evidence { get; }
    public string? Purl { get; }
    public string? KnownLicense { get; }
    public IReadOnlyList<VulnerabilityReference> Vulnerabilities { get; init; } = [];

    public IdentifiedComponent WithVulnerabilities(IReadOnlyList<VulnerabilityReference> vulns);
    public IReadOnlyDictionary<SignalKind, int> EvidenceBySignal { get; }  // grouped, for reports
}
```

> **Evidence invariant (FR-014, SC-007), enforced in the constructor:** `evidence.Count == 0` throws
> `ArgumentException`. **A component with no evidence cannot exist as a matter of type construction**
> — this is the single strongest guarantee in the API: no caller-side check can accidentally skip it.

### `VersionResolution`

```csharp
public sealed record VersionResolution
{
    public required VersionKind Kind { get; init; }     // Exact | Range
    public string? Exact { get; init; }                  // set iff Kind == Exact
    public string? RangeLow { get; init; }                // set iff Kind == Range
    public string? RangeHigh { get; init; }               // set iff Kind == Range
    public IReadOnlyList<EvidenceRecord> Basis { get; init; } = [];

    public static VersionResolution OfExact(string version, IReadOnlyList<EvidenceRecord> basis);
    public static VersionResolution OfRange(string low, string high, IReadOnlyList<EvidenceRecord> basis);
    public string Display { get; }   // "1.2.11" or "1.2.8–1.2.11"
}
```

> **Precision invariant (FR-013):** a version is never reported more precisely than its evidence
> supports. `VersionResolver.FromFunctions` resolves the library version to the **intersection** of the
> present functions' known version ranges (tighter than a naive union); if the intersection collapses
> (genuinely conflicting evidence), it falls back to the **enclosing union** rather than fabricate a
> point value. String-signal resolution (`StringEvidenceMatcher.ResolveVersion`) follows the same
> honesty rule: one exact-version string ⇒ exact; multiple conflicting exact strings ⇒ range spanning
> them; only range-tagged strings ⇒ their bound intersection; no version evidence at all ⇒ the
> library's full known window. Versions are ordered numerically by `Util.VersionOrder` (dotted-numeric
> segments + trailing letter suffix), never as raw strings — `"1.2.11"` correctly sorts after
> `"1.2.9"`.

### `EvidenceRecord`

```csharp
public sealed record EvidenceRecord
{
    public required EvidenceKind Kind { get; init; }     // MatchedString | MatchedConstant | MatchedFunction | PresentSymbol | VersionString
    public required SignalKind Signal { get; init; }     // StringConstant | CfgShape | NormInsn | Embedding | Symbol
    public required string Detail { get; init; }          // human-readable — the matched string, or "target fn @0x... ≈ lib:fn (sim 0.83)"
    public double Strength { get; init; } = 1.0;           // 0..1 contribution to confidence
}
```

The concrete basis for every component and version claim (FR-014/027, Principle XII "Explain Every
Number"). No component and no version claim exists without at least one `EvidenceRecord`.

### `UnidentifiedRegion`

```csharp
public sealed record UnidentifiedRegion
{
    public required ulong StartAddress { get; init; }
    public required ulong EndAddress { get; init; }
    public required UnidentifiedReason Reason { get; init; }  // NoMatch | LowConfidence | RecoveryUncertain | Packed
    public IReadOnlyList<int> FunctionIds { get; init; } = [];
}
```

> **Honesty invariant (FR-015, SC-008):** every recovered function is covered either by a matched
> component's evidence, or by an `UnidentifiedRegion` — no silent drops, no forced matches. With
> function recovery available, `CompositeMatcher` reports regions at per-function granularity; when no
> functions were recovered (e.g. an unsupported architecture), it falls back to the whole-file region
> the string matcher computes.

### `VulnerabilityReference`

```csharp
public sealed record VulnerabilityReference
{
    public required string Id { get; init; }
    public required VulnSource Source { get; init; }        // Osv | Nvd
    public bool AppliesToRange { get; init; }                 // true when the component version is a range and the vuln applies across it
    public string? Severity { get; init; }
    public string? SnapshotVersion { get; init; }             // pinned vuln-data snapshot, for deterministic scans
}
```

Attached post-scan by `Strata.Vuln.VulnerabilityCrossReference` (a thin OSV/NVD range-aware lookup,
outside `Strata.Core`) via `IdentifiedComponent.WithVulnerabilities`. Deep triage/reachability is
explicitly out of scope (FR-019) — identification is the value.

## Corpus access — `ICorpus`

```csharp
public interface ICorpus
{
    CorpusManifest Manifest { get; }
    IReadOnlyList<CorpusStringSignature> StringSignatures { get; }
    IReadOnlyList<CorpusFunctionSignature> FunctionSignatures { get; }
    int DistinctiveStringCount(string libraryName);
    int FunctionCount(string libraryName);
}
```

`Strata.Core.Corpus.InMemoryCorpus` is the in-process implementation (backs unit tests, the web demo's
bundled seed corpus, and is the shape `Strata.Corpus.SqliteCorpus` hydrates into). A production corpus
is loaded from SQLite by the sibling `Strata.Corpus` package (outside `Strata.Core`, per
`contracts/signature-db.md`) but exposed to the engine only through this interface.

```csharp
public sealed record CorpusManifest
{
    public required string CorpusVersion { get; init; }
    public required int SchemaVersion { get; init; }
    public required int LibraryCount { get; init; }
    public string? ModelVersion { get; init; }   // null/"parked" when heuristics-only (SC-004)
}

public sealed record CorpusStringSignature
{
    public required string LibraryName { get; init; }
    public required string Value { get; init; }
    public required double Distinctiveness { get; init; }   // 0..1; higher = more unique to this library
    public string? Purl { get; init; }
    public string? KnownLicense { get; init; }
    public string? ExactVersion { get; init; }                // set when the string uniquely identifies one version
    public string? VersionLow { get; init; }
    public string? VersionHigh { get; init; }
}

public sealed record CorpusFunctionSignature
{
    public required string LibraryName { get; init; }
    public required string FunctionName { get; init; }
    public required ulong CfgShapeHash { get; init; }
    public required IReadOnlyList<uint> NormInsnMinHash { get; init; }
    public IReadOnlyList<float>? Embedding { get; init; }      // null when the model is parked (SC-004)
    public double Distinctiveness { get; init; } = 1.0;
    public string? ExactVersion { get; init; }
    public string? VersionLow { get; init; }
    public string? VersionHigh { get; init; }
}
```

`SchemaVersion` is checked against `StrataInfo.SupportedCorpusSchemaVersion` (currently `1`) at the top
of every `Scan` call — a mismatch throws `CorpusSchemaMismatchException` before any binary parsing
happens.

## Errors (`Strata.Core.Errors`)

```csharp
public abstract class StrataException : Exception { }

public sealed class UnsupportedFormatException : StrataException { }   // corrupt/unrecognised binary (FR-004)
public sealed class CorpusSchemaMismatchException : StrataException    // corpus built for a schema this build doesn't support
{
    public int Found { get; }
    public int Supported { get; }
}
public sealed class OutOfEnvelopeException : StrataException { }        // input exceeds LoadOptions.MaxInputBytes
```

All three derive from `StrataException`, so a caller that only needs "did the engine refuse this
input" can catch the base type; callers that need to branch on *why* (e.g. the CLI mapping to distinct
exit codes) catch the concrete types. None of these are thrown for a malformed-but-parseable binary —
readers degrade to header-only/best-effort facts instead (see `docs/security-review.md` §(a)).

## `StrataInfo`

```csharp
public static class StrataInfo
{
    public const string Version = "0.1.0";
    public const int SupportedCorpusSchemaVersion = 1;
}
```

## Usage: scan a binary in C#

This mirrors exactly what `Strata.Cli.ScanCommand` does — the CLI has no logic beyond argument parsing,
report/SBOM emission, and exit-code mapping; everything shown below is real `Strata.Core` API.

```csharp
using Strata.Core;
using Strata.Core.Corpus;
using Strata.Core.Errors;
using Strata.Core.Model;
using Strata.Corpus; // SeedCorpus — the bundled, in-memory seed corpus

ICorpus corpus = SeedCorpus.AsCorpus();
var scanner = new StrataScanner();

var options = new ScanOptions
{
    Load = new LoadOptions { MaxInputBytes = 200 * 1024 * 1024 }, // set an explicit cap for untrusted input
    Match = new MatchOptions { MinConfidence = 0.5 },
};

try
{
    using FileStream fs = File.OpenRead("firmware.bin");
    ScanResult result = scanner.Scan(fs, "firmware.bin", corpus, options,
        progress: new Progress<ScanProgress>(p => Console.Error.WriteLine($"[{p.Stage}] {p.Message}")));

    foreach (IdentifiedComponent c in result.Components)
    {
        Console.WriteLine($"{c.LibraryName} {c.Version.Display}  confidence={c.Confidence:F2}");
        foreach (EvidenceRecord e in c.Evidence)
        {
            Console.WriteLine($"  - [{e.Signal}] {e.Detail}");
        }
    }

    foreach (UnidentifiedRegion u in result.UnidentifiedRegions)
    {
        Console.WriteLine($"unidentified: 0x{u.StartAddress:x}-0x{u.EndAddress:x} ({u.Reason})");
    }

    foreach (string warning in result.Warnings)
    {
        Console.WriteLine($"warning: {warning}");
    }
}
catch (UnsupportedFormatException ex)
{
    Console.Error.WriteLine($"not a recognised binary: {ex.Message}");
}
catch (OutOfEnvelopeException ex)
{
    Console.Error.WriteLine($"input too large: {ex.Message}");
}
catch (CorpusSchemaMismatchException ex)
{
    Console.Error.WriteLine($"corpus/tool version mismatch: found v{ex.Found}, supports v{ex.Supported}");
}
```

To feed a `ScanResult` into an SBOM, pass it to `Strata.Sbom.SbomWriter.Emit(result, SbomFormat.CycloneDx, options)`
— outside `Strata.Core`, covered by `specs/001-strata-binary-sbom/contracts/sbom-output.md`.
