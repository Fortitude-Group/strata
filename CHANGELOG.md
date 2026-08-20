# Changelog

All notable changes to Strata are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning follows
[SemVer](https://semver.org/) (Principle II).

> **Status note:** this is a **pre-release R&D build**. The repository is local-only — not yet pushed
> to a remote — per the owner's instruction to build the full app privately first. `0.1.0` marks the
> state of that private build, not a public release.

## [0.1.0] - 2026-08-20

Initial build: the full binary-to-SBOM pipeline, end to end, for x86-64 and AArch64, across ELF/PE/Mach-O,
with a validated Checkpoint A pass on real cross-compiler zlib.

### Added

**Engine (`Strata.Core`)**
- Container ingestion for ELF, PE/COFF, and Mach-O (64-bit, both endiannesses where applicable):
  tolerant, best-effort header/section/symbol parsing that degrades to header-only facts on a malformed
  table rather than failing the scan — stripped and odd binaries are the common case, not the exception.
- Whole-file printable-string extraction and Shannon-entropy / known-signature packing detection
  (UPX, ASPack, PECompact) — packed binaries are flagged, never unpacked.
- x86-64 instruction decoding via **Iced** (pure C#, AOT-friendly, no native dependency) and AArch64
  decoding via a **Capstone** native binding, behind a common `IInstructionDecoder` abstraction.
- First-party function-boundary recovery: linear-sweep decode with symbol- and call-target-derived
  entry points, plus basic-block CFG construction (`CfgBuilder`) from the resulting instruction stream.
- Three independent fingerprint signals per recovered function, so they fail independently: a
  string/constant signal, a register-allocation-robust **CFG-shape hash**, and a **MinHash** over
  normalised-instruction n-grams — all pure functions of function bytes + architecture (Principle IV
  determinism). Corpus and target functions are fingerprinted by the *same* code (Principle I).
- Banded MinHash-**LSH** candidate retrieval (`LshIndex`) plus exact CFG-shape hashing as a second
  recall channel, feeding a composite matcher that fuses the string and function signals by
  **noisy-OR** — independent-signal agreement strengthens a claim rather than diluting it.
- **Version resolution** (`VersionResolver`) by intersection of matched functions' known version
  ranges, falling back to the enclosing union on genuinely conflicting evidence — never more precise
  than the evidence supports (FR-013).
- Hard, type-enforced invariants: an `IdentifiedComponent` cannot be constructed with empty evidence
  (FR-014/SC-007), and every recovered function is accounted for by either a component's evidence or an
  `UnidentifiedRegion` (FR-015/SC-008).
- Structured JSON-lines telemetry (`StructuredLog`), off by default, for tracing scan stages/timings
  without touching the deterministic scan result.

**Learned-embedding pipeline (Checkpoint B) — built, measured, parked**
- End-to-end learned-similarity path: `OpcodeHistogram` feature extraction, an `EmbeddingModel` wrapper
  over **ONNX Runtime** in-process inference, an embedding-cosine channel in the function matcher,
  corpus schema/writer/reader support for per-function embeddings, a symbol-labelled training-data
  exporter, and a from-scratch numpy contrastive trainer (no PyTorch dependency) producing a portable
  ONNX model.
- Measured against SC-004 on real zlib (1,593 training examples, 149 functions): on the hardest
  cross-optimisation case (`-O0` clang target vs. `-O2/-O3/-Os` gcc corpus) the embedding added **0**
  matches over the heuristic signals — below the +5-point recall-gain bar. **Decision: parked.** Strata
  ships heuristics-first; the inference wiring, corpus storage, and `--model` flag remain in place so a
  stronger model can be dropped in and re-measured without code changes.

**Version and vulnerability data**
- Corpus signature model (`ICorpus`, `CorpusStringSignature`, `CorpusFunctionSignature`,
  `CorpusManifest`) with an in-memory implementation and a SQLite-backed store/writer/reader
  (`Strata.Corpus`) for a persisted, versioned signature database.
- Thin CVE cross-reference (`Strata.Vuln`) mapping identified `library@version` pairs to known CVEs
  from a pinned OSV snapshot, range-aware for version-range components (FR-019). Deep triage is
  explicitly out of scope.

**Output**
- **CycloneDX (1.6, JSON)** and **SPDX (2.3, JSON)** emitters, each carrying per-component evidence and
  confidence, plus human-readable (Spectre.Console-rendered text) and machine-readable (JSON) reports —
  both including the unidentified-regions section (FR-016/017).
- **FR-018 compliance-claim guard**: every emitted SBOM/report is checked, at emit time, against a
  denylist of CRA-compliance-assertion phrasings; a violation throws rather than ships. Strata reports
  evidence toward an SBOM — it does not certify compliance.

**Tooling**
- `strata` CLI (`scan`, `corpus [verify]`, `version`) with pipeline-gating exit codes
  (`0` success · `1` error · `2` findings need attention · `3` usage).
- Reproducible, containerised corpus-build farm (`tools/corpus-builder`) and a held-out
  precision/recall/version-accuracy benchmark harness (`benchmark/Strata.Benchmark`) with published
  kill-criteria gates (SC-002/003/004/010).
- **Checkpoint A validated on real cross-compiler zlib**: corpus built from gcc `-O2/-O3/-Os` across
  four zlib versions, held out against the same versions built with clang `-O0` and stripped. Result:
  **100% precision, 100% recall, 100% version accuracy** — published in `docs/benchmarks/`, including
  the honest caveat that on the hardest O0-vs-O2+ case identification currently rides on the string
  signal (zlib's embedded strings survive stripping), which is exactly the gap Checkpoint B's embedding
  was aimed at (and, on this corpus, did not close).
- Public Blazor Server web demo (`Strata.Web`): sample-first with an optional capped upload (8 MiB),
  strictly in-memory / no retention, per-IP scan throttling, no authentication, running the identical
  `IScanner` the CLI uses so identical bytes produce identical results in both places.
- CI (`dotnet build`/`test`/`format` gate on every push/PR) and a release workflow publishing
  self-contained single-file executables for `linux-x64`, `linux-arm64`, `win-x64`, `osx-arm64`, plus a
  runtime-free container image; a GitHub Action (`action.yml`) wrapping the container image for
  CI-native scanning with a configurable `fail-on` policy.
- 35 automated tests green across `Strata.Core`, `Strata.Cli`, `Strata.Corpus`, and `Strata.Sbom` test
  projects; solution-wide `dotnet format` clean; build treats warnings as errors.

### Changed

- Function recovery upgraded from purely heuristic (call-target/section-start) entry points to
  symbol-aware recovery: parsing ELF `.symtab`/`.dynsym` `STT_FUNC` entries when the binary is not
  stripped raised corpus function coverage from 352 to 1,605 signatures on the same real zlib corpus.
- PE and Mach-O ingestion (Phase 10) brought the engine from ELF-only to all three container formats
  targeted by the spec, alongside the version-intersection resolver (US2).

### Notes — deliberate deviations from the original plan

- **CLI argument parsing is hand-rolled** (`Strata.Cli.ArgMap`), not `System.CommandLine` as originally
  planned (`specs/001-strata-binary-sbom/plan.md`). A minimal positional + `--flag`/`--key value`
  parser was sufficient for the CLI's actual surface and avoided an extra dependency; revisit if the
  CLI's argument surface grows materially.
- **FluentAssertions was dropped** from the test dependency set as originally pinned
  (`tasks.md` T008) because it is now a commercially licensed package incompatible with Strata's
  Apache-2.0, dependency-light posture. Tests use xUnit's built-in assertions instead.
- **AArch64 decoding, PE/Mach-O ingestion, and the learned-embedding signal** were all originally
  tracked as "in progress" in the README's Status section; as of this build all three have shipped —
  AArch64 via Capstone, PE/Mach-O readers, and the embedding pipeline (measured and parked, not merely
  stubbed).
- **Repository not yet pushed to a remote.** This build is local-only, per explicit owner instruction
  to complete the private build first; treat version `0.1.0` as an internal milestone, not a tagged
  public release.
