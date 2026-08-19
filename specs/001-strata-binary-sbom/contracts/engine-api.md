# Contract: `Strata.Core` engine API

**Stability**: Public library API — SemVer'd (Principle II). This is the one implementation of the
matching pipeline; CLI, web, benchmark, and corpus-builder all consume it (Principle I).

## Primary surface (conceptual signatures)

```csharp
// Ingestion (FR-001..005)
public interface IBinaryLoader {
    ScanTarget Load(Stream binary, LoadOptions options);   // throws UnsupportedFormatException (→ exit 1)
}

// Function recovery (FR-006)
public interface IFunctionRecovery {
    IReadOnlyList<RecoveredFunction> Recover(ScanTarget target, RecoveryOptions options);
}

// Fingerprinting (FR-007/008) — same code used to build the corpus (Principle I)
public interface IFingerprinter {
    FunctionSignature Fingerprint(RecoveredFunction fn, ScanTarget target);
}

// Matching + version + confidence (FR-012/013/014)
public interface IMatcher {
    ScanResult Match(ScanTarget target,
                     IReadOnlyList<FunctionSignature> signatures,
                     ICorpus corpus,
                     MatchOptions options);   // MatchOptions carries the pluggable confidence combiner (R9)
}

// Orchestration — the whole pipeline, streaming progress for the web demo (FR-024)
public interface IScanner {
    ScanResult Scan(Stream binary, ScanOptions options,
                    IProgress<ScanProgress>? progress = null);
}

// Corpus access (contracts/signature-db.md)
public interface ICorpus {
    CorpusManifest Manifest { get; }
    IEnumerable<CorpusMatch> Lookup(FunctionSignature signature, LookupOptions options);
}
```

## Guarantees
- **Determinism** (Principle IV): `Scan` is a pure function of (binary bytes, corpus, options); no ambient
  time/RNG affects `ScanResult`. `ScanProgress` events are for UX only and never alter the result.
- **Evidence invariant** (FR-014, SC-007): `IMatcher` never returns an `IdentifiedComponent` with empty
  evidence; construction throws if attempted.
- **Honesty invariant** (FR-015, SC-008): every `RecoveredFunction` is covered by component evidence or an
  `UnidentifiedRegion` — asserted by `IScanner` before returning.
- **Streaming** (FR-024, SC-006): components surface via `IProgress<ScanProgress>` as they are confirmed,
  enabling the "libraries light up" demo and first-result-under-20s.
- **No runtime dependency leakage** (FR-021): the engine API and its default implementation require no
  external process; the Ghidra/r2 plug-in and ONNX model are opt-in and isolated behind interfaces.

## Errors
`UnsupportedFormatException`, `CorpusSchemaMismatchException`, `OutOfEnvelopeException` (input beyond the
supported size envelope — reported, not hung). All map to CLI exit codes in `contracts/cli.md`.
