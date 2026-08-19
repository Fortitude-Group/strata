# Phase 1 Data Model — Strata Binary-to-SBOM

Domain entities derived from the spec's Key Entities and Functional Requirements. These are the
engine's in-memory/domain types (namespace `Strata.Core.Model`) and the persisted corpus schema.
Wire/output shapes (CycloneDX/SPDX, DB tables) are specified in `contracts/`.

## Entity overview & relationships

```text
ScanTarget 1───* RecoveredFunction 1───* FunctionSignature
   │                                          │ (matched against)
   │                                          ▼
   │                                   CorpusSignature *───1 CorpusFunction *───1 CorpusLibraryVersion *───1 CorpusLibrary
   ▼
ScanResult 1───* IdentifiedComponent 1───1 VersionResolution
             │                       1───* EvidenceRecord
             1───* UnidentifiedRegion
             1───* VulnerabilityReference   (attached to IdentifiedComponent)

Corpus 1───* CorpusLibrary ; Corpus 1───1 CorpusManifest
BenchmarkCase *───1 BenchmarkGroundTruth ; BenchmarkRun 1───* BenchmarkCaseResult
```

---

## Scan-side entities

### ScanTarget
The input artefact under analysis.
| Field | Type | Notes |
|-------|------|-------|
| `Path` / `Bytes` | string / byte[] | Source of the binary (file or in-memory upload) |
| `Format` | enum {Elf, Pe, MachO, Unknown} | Detected container format (FR-001) |
| `Architecture` | enum {X86_64, AArch64, Other} | Detected arch (FR-003) |
| `Sections` | Section[] | name, offset, size, flags |
| `EntryPoints` | ulong[] | (FR-003) |
| `ImportedSymbols` / `ExportedSymbols` | Symbol[] | present only if not fully stripped (FR-003) |
| `Strings` | StringLiteral[] | value + address; evidence source (FR-003) |
| `Constants` | ConstantBlob[] | tables/magics; address + bytes (FR-003) |
| `Linkage` | enum {Static, Dynamic, Mixed, Unknown} | affects evidence weighting |
| `PackingStatus` | enum {NotPacked, Packed, Suspected} | packing/obfuscation flag (FR-005) |
**Validation**: `Format == Unknown` ⇒ reject with actionable error (FR-004). `PackingStatus != NotPacked`
⇒ flag and do not attempt unpack; results marked non-authoritative (FR-005).

### RecoveredFunction
A function boundary recovered from the target (FR-006).
| Field | Type | Notes |
|-------|------|-------|
| `Id` | int | stable within a scan |
| `StartAddress` / `EndAddress` | ulong | recovered boundary |
| `BasicBlocks` | BasicBlock[] | CFG nodes |
| `Edges` | (int,int)[] | CFG edges |
| `RecoveryConfidence` | float 0–1 | layered-heuristic confidence (R4) |
| `RecoverySource` | enum {Symbol, CallTarget, Prologue, LinearSweep, Plugin} | provenance |
**State**: `Discovered → Disassembled → CfgBuilt → Fingerprinted`.

### FunctionSignature
The multi-signal fingerprint of one recovered function (FR-007).
| Field | Type | Notes |
|-------|------|-------|
| `FunctionId` | int | → RecoveredFunction |
| `StringConstRefs` | hash-set | referenced strings/constants (signal a) |
| `CfgShapeHash` | ulong | block count, edge structure, loop nesting (signal b) |
| `NormInsnMinHash` | uint[] | MinHash of normalised opcode n-grams (signal c) |
| `Embedding` | float[]? | optional learned vector (signal d, R7); null if model parked |
**Determinism**: all signals are pure functions of the function bytes + arch (Principle IV).

### ScanResult
The top-level output of a scan.
| Field | Type | Notes |
|-------|------|-------|
| `Target` | ScanTarget | back-reference |
| `Components` | IdentifiedComponent[] | (FR-012) |
| `UnidentifiedRegions` | UnidentifiedRegion[] | (FR-015) |
| `CorpusVersion` | string | pinned corpus used (Principle II) |
| `ToolVersion` | string | Strata version |
| `Warnings` | string[] | e.g. packing detected, out-of-envelope |

### IdentifiedComponent
A library attributed to the target — the SBOM component (FR-012/014).
| Field | Type | Notes |
|-------|------|-------|
| `LibraryName` | string | canonical name |
| `Purl` | string | package URL (`pkg:generic/...`) where derivable |
| `KnownLicense` | string? | passed through from corpus metadata (non-goal to analyse) |
| `Confidence` | float 0–1 | distinctiveness-weighted coverage (R9) |
| `Version` | VersionResolution | (FR-013) |
| `Evidence` | EvidenceRecord[] | ≥1 required (FR-014); component invalid if empty |
| `Vulnerabilities` | VulnerabilityReference[] | thin cross-ref (FR-019) |
**Validation**: `Evidence.Length >= 1` (FR-014, SC-007) — a component with no evidence MUST NOT exist.

### VersionResolution
| Field | Type | Notes |
|-------|------|-------|
| `Kind` | enum {Exact, Range} | (FR-013) |
| `Exact` | string? | set iff Kind==Exact |
| `RangeLow` / `RangeHigh` | string? | inclusive bounds iff Kind==Range |
| `Basis` | EvidenceRecord[] | version-distinguishing evidence (R8) |
**Validation**: reported precision ≤ evidence precision (FR-013); range MUST contain the true version by
construction and be no narrower than `Basis` supports (SC-003).

### EvidenceRecord
The concrete basis for a component or version claim (FR-014/027, Principle XII).
| Field | Type | Notes |
|-------|------|-------|
| `Kind` | enum {MatchedFunction, MatchedString, MatchedConstant, PresentSymbol, VersionString} | |
| `Detail` | string | e.g. function address ↔ corpus function name, matched string value |
| `Signal` | enum {StringConst, CfgShape, NormInsn, Embedding, Symbol} | which signal fired |
| `Strength` | float 0–1 | contribution to confidence |

### UnidentifiedRegion
Code not attributed to any corpus library (FR-015, SC-008).
| Field | Type | Notes |
|-------|------|-------|
| `StartAddress` / `EndAddress` | ulong | region bounds |
| `FunctionIds` | int[] | recovered functions in the region |
| `Reason` | enum {NoMatch, LowConfidence, RecoveryUncertain, Packed} | why unplaced |

### VulnerabilityReference
| Field | Type | Notes |
|-------|------|-------|
| `Id` | string | CVE / OSV id |
| `Source` | enum {Osv, Nvd} | (R13) |
| `AppliesToRange` | bool | true when component version is a range (US5 sc.2) |
| `Severity` | string? | as published |
| `SnapshotVersion` | string | pinned vuln-data snapshot for determinism |

---

## Corpus-side entities (persisted — see `contracts/signature-db.md`)

### Corpus / CorpusManifest
| Field | Type | Notes |
|-------|------|-------|
| `Version` | string | corpus artefact version (SemVer) |
| `SchemaVersion` | int | DB schema contract version (Principle II) |
| `Libraries` | CorpusLibrary[] | included libraries |
| `Toolchains` | ToolchainRef[] | gcc/clang versions + image hashes (reproducibility, SC-009) |
| `BuildFlagsMatrix` | string[] | opt levels, arches |
| `ModelVersion` | string? | ONNX embedding model version, if embeddings included |

### CorpusLibrary → CorpusLibraryVersion → CorpusFunction → CorpusSignature
| Entity | Key fields |
|--------|-----------|
| `CorpusLibrary` | `Name`, `Purl`, `KnownLicense`, `SourceUrl` |
| `CorpusLibraryVersion` | `Version`, `Cve[]` (versions flagged for CVE inclusion), `BuildVariants` |
| `CorpusFunction` | `Name`, `Distinctiveness` (inverse cross-library frequency, R9) |
| `CorpusSignature` | per (function × compiler × optLevel × arch): `StringConstRefs`, `CfgShapeHash`, `NormInsnMinHash`, `Embedding?` |
**Validation**: signature extraction uses the **same** code path as scan-time fingerprinting (Principle I —
one implementation). A reproducible rebuild yields equivalent `CorpusSignature` rows (SC-009).

---

## Benchmark entities

### BenchmarkCase / BenchmarkGroundTruth
| Field | Type | Notes |
|-------|------|-------|
| `Binary` | path | held-out binary (FR-022) |
| `Toolchain` | string | deliberately ≠ corpus toolchains |
| `TrueComponents` | (LibraryName, Version)[] | ground truth |

### BenchmarkRun / BenchmarkCaseResult
| Field | Type | Notes |
|-------|------|-------|
| `Checkpoint` | enum {A, B} | which thresholds evaluated (SC-001/002/003) |
| `Precision` / `Recall` | float | top-1 library identification, per lib + aggregate |
| `VersionAccuracy` | float | fraction resolved to correct minor (SC-003) |
| `ScanWallTimeMs` | long | vs SC-005 |
| `Passed` | bool | vs checkpoint thresholds (FR-023) |
| `Published` | bool | results published regardless of outcome (SC-010) |

---

## Cross-cutting rules

- **Evidence invariant** (FR-014/027, SC-007): no `IdentifiedComponent` and no `VersionResolution` may
  exist without a non-empty evidence/basis set. Enforced at construction and asserted in tests.
- **Honesty invariant** (FR-015, SC-008): every recovered function is either inside an
  `IdentifiedComponent`'s evidence or accounted for by an `UnidentifiedRegion`. No function is silently
  dropped or force-matched.
- **Determinism invariant** (Principle IV): `ScanResult` is a pure function of (ScanTarget bytes,
  CorpusVersion, ToolVersion, vuln-SnapshotVersion). Timestamps/serial numbers are output-layer, opt-in.
- **Precision invariant** (FR-013): a version is never reported more precisely than its `Basis` supports.
