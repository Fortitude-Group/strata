# Implementation Plan: Strata — Binary-to-SBOM

**Branch**: `001-strata-binary-sbom` | **Date**: 2026-08-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/001-strata-binary-sbom/spec.md`

## Summary

Strata scans a compiled, stripped, possibly statically-linked native binary and produces an
evidence-backed CycloneDX/SPDX SBOM of the open-source libraries and versions compiled into it,
each with a confidence score and the concrete evidence behind it, plus an honest report of code it
could not attribute.

**Technical approach**: A single, first-party **engine library** (`Strata.Core`) does ingestion →
function recovery → multi-signal fingerprinting → corpus matching → version resolution → confidence
scoring. It is consumed identically by a self-contained **CLI** (the primary CI-facing surface), a
**web demo**, and a **benchmark harness**. A reproducible **corpus builder** compiles curated
open-source libraries across compilers × optimisation levels in containers and extracts the same
fingerprints into a versioned, indexed **signature database**. Matching is **heuristics-first**
(string/constant, CFG-shape, normalised-instruction signals) to clear Checkpoint A before any ML;
an optional learned-similarity signal (trained in Python, shipped as portable ONNX inference)
is added only if the benchmark shows it earns its place (Checkpoint B, SC-004). The stack is
**.NET (C#)** so the user-facing tool ships as a single self-contained executable with no separate
runtime install, honouring the author's stated constraint.

## Technical Context

**Language/Version**: C# on **.NET 10** (LTS-track, current), targeting `net10.0`. ML training only
(off the shipping path) uses **Python 3.12**.

**Primary Dependencies** (all pinned; each justified in [research.md](./research.md)):
- **Disassembly/decoding**: `Iced` (pure-C# x86/x86-64 decoder, no native dep) for x86-64; **Capstone**
  via bindings for AArch64. Optional depth plug-in shells out to a headless disassembler (Ghidra/radare2)
  — not a hard dependency.
- **Binary parsing**: first-party ELF/PE/Mach-O readers in `Strata.Core.Ingestion` (thin, we need only
  headers/sections/symbols/strings), with `ELFSharp`/`LibObjectFile` evaluated as a fallback reference.
- **Signature store**: **SQLite** (embeddable, single-file, ships inside the CLI) for structured
  signatures + a portable **ANN/LSH index** (HNSW for embeddings, MinHash-LSH for fuzzy heuristic
  matching) persisted alongside.
- **ML inference**: **ONNX Runtime** (native lib bundled in the self-contained build) for the optional
  embedding signal. Training: PyTorch in `tools/ml-training`, exported to ONNX — never on the ship path.
- **SBOM**: `CycloneDX` .NET library for CycloneDX JSON; first-party SPDX (tag-value + JSON) emitter.
- **Vulnerability data**: **OSV** as primary (clean library@version→CVE API and downloadable ranges),
  NVD as secondary enrichment; queried offline against a cached snapshot for determinism.
- **CLI**: `System.CommandLine` for parsing; `Spectre.Console` for the human-readable report.
- **Web demo**: **ASP.NET Core + Blazor Server** (SignalR under the hood) for the progressive
  "libraries light up" streaming; reuses `Strata.Core` directly.

**Storage**: Signature database = SQLite file + sidecar ANN/LSH index, versioned as a publishable
artefact. No runtime user database. Web demo holds uploads **in memory only**, deleted after response.

**Testing**: `xUnit` for unit/contract/integration; golden-file tests for SBOM output determinism;
the **benchmark harness** (`Strata.Benchmark`) is a separate, first-class measurement tool, not a
unit-test suite. Small known-composition sample binaries are built as fixtures.

**Target Platform**: Cross-platform. Primary = **Linux x64/arm64** (where CI runs), delivered as a
container image and a self-contained single-file executable; also self-contained executables for
Windows x64 and macOS (arm64/x64) for developer laptops. Web demo = Linux container on Fortitude
Omnis R&D hosting.

**Project Type**: Multi-project .NET solution — a **core engine library** consumed by a **CLI**, a
**web app** (demo), a **benchmark harness**, and out-of-band **corpus-builder + ml-training tools**.

**Performance Goals**: A 40 MB stripped binary scans end-to-end in **< 5 min on a developer laptop**
(SC-005); the web demo returns first results in **< 20 s** for bundled samples (SC-006).

**Constraints**: Self-contained single executable, **no separate runtime install** for the user-facing
tool (author constraint); deterministic SBOM output (Principle IV) with nondeterministic fields
(serial number, timestamp) opt-in/pinnable for golden tests; honest evidence on every claim
(FR-014/027, Principle XII); reproducible corpus builds (SC-009); no unpacking, no decompilation.

**Scale/Scope**: Initial corpus ≈ **50 libraries** × 2 compilers (gcc, clang) × 4 opt levels
(-O0/-O2/-O3/-Os) × selected versions, growing to a few hundred libraries. Target binaries up to
~40 MB in the performance envelope; larger inputs reported as out-of-envelope rather than hanging.

## Constitution Check

*GATE: evaluated against `.specify/memory/constitution.md` v1.5.0. Must pass before Phase 0 and again
after Phase 1.*

| Principle | Status | How this plan satisfies it |
|-----------|--------|----------------------------|
| **I. Modular & Composable** | ✅ PASS | One engine (`Strata.Core`) behind an explicit API; CLI, web, benchmark, corpus-builder are thin consumers. No shared logic is forked — matching/fingerprinting live in exactly one place (FR-007/012). |
| **II. Contract Stability & SemVer** | ✅ PASS | Public contracts identified and versioned in `contracts/`: CLI command surface, SBOM output schema + evidence extension, signature-DB format, `Strata.Core` API. Corpus/DB format carries its own version; consumers pin it. |
| **III. Comprehensive Tests for Public Contracts** | ✅ PASS | xUnit coverage for engine API, CLI schema, SBOM validity (CycloneDX/SPDX validators) + golden output, DB format, and edge cases (FR-004/005, unrecognised/packed/empty inputs). Benchmark measures accuracy separately. Test-first optional (Principle III). |
| **IV. Deterministic & Observable** | ✅ PASS | Same binary + same corpus version ⇒ identical SBOM. Nondeterministic SBOM fields (serialNumber, timestamp) are opt-in and pinnable for golden tests. Structured logging + per-stage telemetry; every claim traceable to its evidence. |
| **V. Simplicity & Justified Complexity** | ✅ PASS (phased) | Inherent complexity (multi-signal matching, ML, corpus farm, web demo) is **requirement-driven and phased**: heuristics-first clears Checkpoint A before any ML is added, and ML ships only if it earns ≥5pts recall (SC-004). Rejected simpler alternatives recorded in research.md. No unjustified abstraction ⇒ Complexity Tracking table empty. |
| **VI. Complete the Scope** | ✅ PASS | Plan maps every FR-001…FR-028 to a component; nothing in-scope deferred. Firm non-goals stay non-goals (Assumptions in spec). |
| **VII. Tracker Is Project of Record** | ⚠️ ACTION | Repo is **not yet a git repository**. Implementation phase MUST `git init`, create the GitHub repo under `Fortitude-Group` as `fortitude-omnis`, and mirror tasks to the tracker with commit↔work-item linkage. Recorded as a Phase-2/implementation precondition, not a design violation. |
| **VIII. Start From a Fresh Base** | ⚠️ ACTION | Same as VII — once the repo exists, branch from a freshly pulled integration tip. No base exists to be stale against yet. |
| **IX. Ask, Then Wait** | ✅ PASS | The one owner-gated decision (licence) was asked and answered (Apache-2.0) before this plan. |
| **X. Production Changes Wait for a Human** | ✅ PASS (noted) | The only "production" surface is web-demo hosting; its deploy requires explicit per-change owner approval and uses a project deploy script (no `--auto-approve`). Corpus builds/CI are ephemeral and exempt. |
| **XI. Establish the Mechanism Before Changing Code** | ✅ PASS | The whole tool is evidence-from-the-system by construction; kill criteria + benchmark decide viability from measured data, not belief (SC-001…SC-004, SC-008). |
| **XII. Explain Every Number** | ✅ PASS | Every confidence score and version claim carries its supporting evidence (FR-014/027); benchmark metrics state what they measure and what follows (pass/fail vs checkpoint). No unexplained figure ships. |

**Gate result (pre-Phase 0)**: PASS. Two ⚠️ items (VII, VIII) are repository-bootstrap actions for the
implementation phase, not design violations — nothing to justify in Complexity Tracking.

**Post-Phase 1 re-check**: PASS (unchanged). The design reinforces the gates rather than straining them —
a single engine behind explicit contracts (I, II), determinism + evidence invariants baked into the
engine API and SBOM contract (IV, XII), and honesty invariants asserted in `data-model.md`. No new
abstraction was introduced beyond what the FRs require, so Complexity Tracking remains empty.

## Project Structure

### Documentation (this feature)

```text
specs/001-strata-binary-sbom/
├── plan.md              # This file
├── research.md          # Phase 0 — technology decisions + rationale
├── data-model.md        # Phase 1 — entities, fields, relationships, state
├── quickstart.md        # Phase 1 — runnable validation scenarios
├── contracts/           # Phase 1 — CLI, SBOM, signature-DB, engine API, web, corpus, action
│   ├── cli.md
│   ├── sbom-output.md
│   ├── signature-db.md
│   ├── engine-api.md
│   ├── web-demo.md
│   ├── corpus-builder.md
│   └── github-action.md
├── checklists/
│   └── requirements.md  # from /speckit-specify
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
Strata.slnx                        # .NET solution (modern XML format)

src/
├── Strata.Core/                   # THE ENGINE (first-party) — consumed by everything below
│   ├── Ingestion/                 # format detection; ELF/PE/Mach-O readers; sections, symbols,
│   │                              #   strings, constants; packing/obfuscation detection (FR-001..005)
│   ├── Recovery/                  # function-boundary recovery, CFG/basic-block construction (FR-006)
│   ├── Disassembly/               # Iced (x86-64) + Capstone (AArch64) adapters; optional Ghidra/r2 plug-in
│   ├── Fingerprinting/            # string/constant, CFG-shape, normalised-instruction, embedding signals (FR-007/008)
│   ├── Matching/                  # corpus lookup, function→library aggregation, confidence scoring (FR-012/014)
│   ├── Versioning/                # version resolution to exact/range with evidence (FR-013)
│   └── Model/                     # shared domain types (Component, Evidence, UnidentifiedRegion, …)
├── Strata.Corpus/                 # signature-DB schema, read/write, ANN+LSH indexing, DB versioning (FR-009/010)
├── Strata.Sbom/                   # CycloneDX + SPDX emitters, reports, evidence attachment (FR-016/017/018)
├── Strata.Vuln/                   # thin OSV/NVD cross-reference over a cached snapshot (FR-019)
├── Strata.Cli/                    # `strata` single-file CLI, exit codes (FR-020/021)
└── Strata.Web/                    # ASP.NET Core + Blazor Server demo, upload cap, throttling (FR-024/025/026)

tools/
├── corpus-builder/                # reproducible Docker build farm: compile libs × compilers × opt,
│   ├── recipes/                   #   per-library recipe + version-selection policy (latest-per-minor + CVE)
│   ├── dockerfiles/               #   pinned toolchain images (gcc, clang)
│   └── Strata.CorpusBuilder/      #   .NET orchestrator that drives builds + signature extraction
└── ml-training/                   # Python 3.12: train similarity/embedding model, export ONNX (off ship path)

benchmark/
└── Strata.Benchmark/              # held-out set runner; precision/recall + version accuracy; kill-criteria eval (FR-022/023)

tests/
├── Strata.Core.Tests/
├── Strata.Corpus.Tests/
├── Strata.Sbom.Tests/
├── Strata.Cli.Tests/
├── contract/                      # CLI schema, SBOM schema/validity, signature-DB format
├── integration/                   # end-to-end scans of known-composition sample binaries
└── fixtures/                      # small, built, known-ground-truth sample binaries
```

**Structure Decision**: Multi-project .NET solution with a **core engine library at the centre**
(Principle I). The CLI is the primary product surface and the only one on the strict single-file /
no-runtime path; the web demo, benchmark, corpus-builder, and ml-training are separate consumers/tools
so their heavier dependencies (ASP.NET, Docker orchestration, Python/PyTorch) never leak into the
shipping CLI. Corpus-builder and ml-training live under `tools/` because they produce *artefacts* (the
signature DB, the ONNX model) rather than being part of the shipped binary.

## Complexity Tracking

> No Constitution Check violations require justification. The table is intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |
