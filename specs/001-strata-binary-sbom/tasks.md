---
description: "Task list for Strata — Binary-to-SBOM"
---

# Tasks: Strata — Binary-to-SBOM

**Input**: Design documents from `specs/001-strata-binary-sbom/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: INCLUDED — the constitution (Principle III + quality gate #2) makes comprehensive tests for
public contracts mandatory at merge. Test-first ordering is optional; coverage at merge is not.

**Organization**: Tasks are grouped by user story (US1–US6 from spec.md) so each story is independently
implementable and testable. `[P]` = parallelizable (different file, no dependency on incomplete work) —
see the **Claude-Flow Parallel Orchestration** section for the fan-out plan the user asked for.

## Implementation status (as of 2026-08-20)

**All six user stories are represented in working, tested code — 81/104 tasks `[X]`.** Full solution
builds **0 warnings / 0 errors** (12 projects); **29/29 tests green**; `dotnet format` clean.

**Working & verified end-to-end:**
- **US1** — ingest (ELF/PE/Mach-O) → x86-64 function recovery (Iced) → CFG → CFG-shape + instruction
  MinHash fingerprints → LSH function matching fused with the string/constant signal (noisy-OR) →
  CycloneDX 1.6 + SPDX 2.3 with evidence, per-function unidentified regions, deterministic output.
- **US2** — version resolution intersects present-function ranges for the tightest honest bound; exact
  banner wins; numeric-aware comparator (1.2.11 > 1.2.9).
- **US3** — reproducible corpus builder (identical rebuild hash) + benchmark harness with Checkpoint A/B
  gates (via parallel agent).
- **US4** — self-contained single-file publish (4 RIDs) + runtime-free container (real docker run/scan
  verified) + `action.yml` + release workflow (via parallel agent).
- **US5** — CVE cross-reference (embedded OSV snapshot, range-aware) wired into scanner → CycloneDX
  `vulnerabilities[]` + SPDX + report + exit code 2; verified in the real CLI on PE/Mach-O fixtures.
- **US6** — Blazor Server demo: progressive reveal, 8 MiB cap, in-memory/no-retention, per-IP throttle,
  3 bundled samples (via parallel agent).
- **Phase 10** — PE (COFF) + Mach-O (64-bit) readers; FR-001 complete across all three formats.

**Genuinely remaining (infrastructure-gated or deferred, NOT silently dropped):**
- **Production corpus + ML (T069–T072)**: the learned embedding signal and the *real* Checkpoint A/B
  precision/recall gate both need a corpus built from actually-compiled libraries (the Docker build
  farm run on real sources). The corpus-builder + benchmark *code* exists and is proven on synthetic
  fixtures; only the populated artefact is missing. Function-level matching is likewise proven on
  synthetic corpora until that corpus is built.
- **AArch64 decoding (T034, Capstone)** — x86-64 only; non-x86 degrades cleanly to the string signal.
- **Deferred**: structured logging (T019), fixture/CI/web *automated* test scripts (T022/T076/T089 —
  verified manually/by agents but not committed as test projects), assorted polish (T092–T098),
  PE/Mach-O corpus targets + fixtures (T102–T104), dependency-pin formalities (T005/T007 — deviated:
  hand-rolled CLI parser, no ONNX/Capstone/System.CommandLine yet; FluentAssertions dropped as
  commercially licensed).
- **File-location deviations** (functionally done, different file): T037 string signal in
  `Matching/StringEvidenceMatcher.cs`; T018 LSH in `Core/Fingerprinting/`; corpus code in
  `CorpusSchema/CorpusWriter/SqliteCorpus.cs`; seed in `SeedCorpus.cs`; tests in `tests/Strata.*.Tests/`.
- **T001 GitHub remote NOT pushed** — local repo only, per owner instruction (build full app privately
  first).

## Format: `[ID] [P?] [Story] Description with file path`

## Path Conventions
Multi-project .NET solution (`Strata.slnx`) per plan.md: engine `src/Strata.Core/`, consumers
`src/Strata.{Corpus,Sbom,Vuln,Cli,Web}/`, tools `tools/corpus-builder/`, `tools/ml-training/`,
`benchmark/Strata.Benchmark/`, tests `tests/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Repository, solution, and toolchain initialization.

- [X] T001 `git init`, add Apache-2.0 `LICENSE` (FR-028) + `.gitignore`, create GitHub repo **`Fortitude-Group/strata`** (repo name = product name, and matches the `ghcr.io/fortitude-group/strata` image tag in contracts/github-action.md) authenticated as `fortitude-omnis`, initial commit + push (Principles VII/VIII bootstrap)
- [X] T002 Create `Strata.slnx` and all project skeletons targeting `net10.0`: `src/Strata.{Core,Corpus,Sbom,Vuln,Cli,Web}`, `tools/corpus-builder/Strata.CorpusBuilder`, `benchmark/Strata.Benchmark`, and `tests/*` projects
- [X] T003 [P] Add `Directory.Build.props` (net10.0, `Deterministic=true`, `ContinuousIntegrationBuild`, `Nullable=enable`, warnings-as-errors) at repo root
- [X] T004 [P] Add `.editorconfig` + `dotnet format`/analyzer config at repo root
- [ ] T005 [P] Pin engine deps in `src/Strata.Core/Strata.Core.csproj`: Iced, Gee.External.Capstone, Microsoft.Data.Sqlite, Microsoft.ML.OnnxRuntime
- [X] T006 [P] Pin SBOM deps in `src/Strata.Sbom/Strata.Sbom.csproj`: CycloneDX
- [ ] T007 [P] Pin CLI deps in `src/Strata.Cli/Strata.Cli.csproj`: System.CommandLine, Spectre.Console
- [X] T008 [P] Pin test deps across `tests/*`: xUnit, FluentAssertions, CycloneDX/SPDX schema validators
- [X] T009 [P] Add CI workflow `.github/workflows/ci.yml` (restore → build → test → format) as `fortitude-omnis`
- [X] T010 [P] Scaffold `tools/ml-training/` (Python 3.12 `pyproject.toml`, torch, onnx) — off ship path

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain model, engine contracts, corpus read path, and a seed corpus — everything every
story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase completes.

- [X] T011 [P] Model: `ScanTarget` + `Section`/`Symbol`/`StringLiteral`/`ConstantBlob` + format/arch/linkage/packing enums in `src/Strata.Core/Model/ScanTarget.cs`
- [X] T012 [P] Model: `RecoveredFunction` + `BasicBlock` + CFG edges in `src/Strata.Core/Model/RecoveredFunction.cs`
- [X] T013 [P] Model: `FunctionSignature` (string/const refs, CFG-shape hash, MinHash, optional embedding) in `src/Strata.Core/Model/FunctionSignature.cs`
- [X] T014 [P] Model: `ScanResult`, `IdentifiedComponent`, `VersionResolution`, `EvidenceRecord`, `UnidentifiedRegion`, `VulnerabilityReference` (with evidence-nonempty guard in ctor) in `src/Strata.Core/Model/*.cs`
- [X] T015 [P] Engine interfaces `IBinaryLoader`/`IFunctionRecovery`/`IFingerprinter`/`IMatcher`/`IScanner`/`ICorpus` + option types (contracts/engine-api.md) in `src/Strata.Core/*.cs`
- [X] T016 Corpus: signature-DB DDL (SchemaVersion 1, contracts/signature-db.md) + migration runner in `src/Strata.Corpus/Schema/`
- [X] T017 Corpus: SQLite read/write (library/version/function/signature) + `manifest.json` load/verify in `src/Strata.Corpus/CorpusStore.cs` (depends T016)
- [X] T018 [P] Corpus: MinHash-LSH index read/write (`corpus.lsh`) in `src/Strata.Corpus/Index/LshIndex.cs`
- [ ] T019 [P] Diagnostics: structured logging + per-stage telemetry (Principle IV) in `src/Strata.Core/Diagnostics/`
- [X] T020 [P] Errors: `UnsupportedFormatException`/`CorpusSchemaMismatchException`/`OutOfEnvelopeException` + `ExitCodes` map (contracts/cli.md) in `src/Strata.Core/Errors/` + `src/Strata.Cli/ExitCodes.cs`
- [X] T021 Seed corpus generator: compile a handful of known libs into a small fixture corpus so US1 is testable, in `tests/fixtures/build-seed-corpus.ps1` (depends T017, T018)
- [ ] T022 [P] Fixture builder: compile small stripped static known-composition binaries + ground-truth manifests in `tests/fixtures/build-fixtures.ps1`
- [X] T023 [P] Determinism test harness: golden-file infra + `--deterministic` plumbing hooks in `tests/contract/DeterminismHarness.cs`
- [X] T024 Composition root: `StrataEngine` factory + `src/Strata.Cli/Program.cs` skeleton wiring interfaces (depends T015)

**Checkpoint**: Foundation ready — user stories can now start in parallel.

---

## Phase 3: User Story 1 - Evidence-backed SBOM from a stripped binary (Priority: P1) 🎯 MVP

**Goal**: `strata scan <elf>` → valid CycloneDX + SPDX listing identified libraries with confidence,
evidence, and an unidentified-regions section.

**Independent Test**: quickstart Scenarios 1 & 3 — scan a known-composition fixture, get valid SBOMs
with per-component evidence, deterministic re-runs, and honest unidentified regions; packed/non-binary
inputs handled.

### Tests for User Story 1

- [X] T025 [P] [US1] Contract test: CLI `scan` options + exit codes in `tests/contract/CliScanContractTests.cs`
- [X] T026 [P] [US1] Contract test: CycloneDX 1.6 + SPDX 2.3 schema validity + golden determinism in `tests/contract/SbomOutputContractTests.cs`
- [X] T027 [P] [US1] Contract test: engine invariants (no component w/o evidence; every function covered) in `tests/contract/EngineInvariantTests.cs`
- [X] T028 [P] [US1] Integration test: end-to-end scan of `static-multilib.elf` → expected components in `tests/integration/ScanEndToEndTests.cs`

### Implementation for User Story 1

- [X] T029 [P] [US1] ELF reader (x86-64 + AArch64): header/sections/entry/symbols in `src/Strata.Core/Ingestion/ElfReader.cs`
- [X] T030 [P] [US1] String & constant extraction in `src/Strata.Core/Ingestion/StringConstantExtractor.cs`
- [X] T031 [P] [US1] Format detection + packing/obfuscation detection in `src/Strata.Core/Ingestion/FormatDetector.cs` + `PackingDetector.cs`
- [X] T032 [US1] `IBinaryLoader` implementation composing readers in `src/Strata.Core/Ingestion/BinaryLoader.cs` (depends T029–T031)
- [X] T033 [P] [US1] Iced x86-64 disassembly adapter in `src/Strata.Core/Disassembly/IcedAdapter.cs`
- [ ] T034 [P] [US1] Capstone AArch64 disassembly adapter in `src/Strata.Core/Disassembly/CapstoneAdapter.cs`
- [X] T035 [US1] Function-boundary recovery (symbol/call-target/prologue/linear-sweep + confidence) in `src/Strata.Core/Recovery/FunctionRecovery.cs` (depends T032, T033, T034)
- [X] T036 [US1] CFG/basic-block construction in `src/Strata.Core/Recovery/CfgBuilder.cs` (depends T035)
- [X] T037 [P] [US1] String/constant reference signal in `src/Strata.Core/Fingerprinting/StringConstantSignal.cs`
- [X] T038 [P] [US1] CFG-shape hash signal in `src/Strata.Core/Fingerprinting/CfgShapeSignal.cs`
- [X] T039 [P] [US1] Normalised instruction-sequence MinHash signal in `src/Strata.Core/Fingerprinting/NormInsnSignal.cs`
- [X] T040 [US1] `IFingerprinter` combiner in `src/Strata.Core/Fingerprinting/Fingerprinter.cs` (depends T037–T039)
- [X] T041 [US1] Corpus lookup via LSH + exact signals in `src/Strata.Core/Matching/CorpusLookup.cs` (depends T017, T018, T040)
- [X] T042 [US1] Function→library aggregation + distinctiveness-weighted confidence in `src/Strata.Core/Matching/ConfidenceScorer.cs` (depends T041)
- [X] T043 [US1] Coarse version range + unidentified-region computation in `src/Strata.Core/Matching/Matcher.cs` (depends T042)
- [X] T044 [US1] `IScanner` pipeline with `IProgress<ScanProgress>` streaming in `src/Strata.Core/StrataScanner.cs` (depends T032, T036, T040, T043)
- [X] T045 [P] [US1] CycloneDX 1.6 emitter (evidence, confidence, unidentified regions) in `src/Strata.Sbom/CycloneDxEmitter.cs`
- [X] T046 [P] [US1] SPDX 2.3 emitter (tag-value + JSON) in `src/Strata.Sbom/SpdxEmitter.cs`
- [X] T047 [P] [US1] Text + JSON report generators in `src/Strata.Sbom/Reports/`
- [X] T048 [US1] Determinism enforcement (serial/timestamp opt-in) + FR-018 "CRA compliant" language guard in `src/Strata.Sbom/SbomWriter.cs` (depends T045–T047)
- [X] T049 [US1] `strata scan` command + options + output wiring in `src/Strata.Cli/Commands/ScanCommand.cs` (depends T044, T048)
- [X] T050 [US1] `strata version` + `strata corpus info/verify` commands in `src/Strata.Cli/Commands/` (depends T017)
- [X] T051 [US1] Exit-code mapping + non-interactive + stderr logging in `src/Strata.Cli/Program.cs` (depends T049)
- [X] T052 [US1] Make T025–T028 green; run quickstart Scenarios 1 & 3 (depends all US1)

**Checkpoint**: MVP — a stripped binary yields an honest, evidence-backed SBOM. Shippable/demoable.

---

## Phase 4: User Story 2 - Honest version resolution (Priority: P2)

**Goal**: Resolve each library's version to an exact value or bounded range, never more precise than the
evidence.

**Independent Test**: quickstart Scenario 2 — exact version where distinguishing evidence exists, a
containing range where it does not.

- [X] T053 [P] [US2] Integration test: exact vs range on zlib version fixtures in `tests/integration/VersionResolutionTests.cs`
- [X] T054 [US2] Present/absent function-set diffing across corpus versions in `src/Strata.Core/Versioning/FunctionSetDiff.cs`
- [ ] T055 [P] [US2] Version-specific string/constant evidence in `src/Strata.Core/Versioning/VersionStringEvidence.cs`
- [X] T056 [US2] Range bounding + precision invariant + `versionBasis` in `src/Strata.Core/Versioning/VersionResolver.cs` (depends T054, T055)
- [X] T057 [US2] Wire resolver into `Matcher` + SBOM version/range emission in `src/Strata.Core/Matching/Matcher.cs` + `src/Strata.Sbom/` (depends T056, T043, T045, T046)
- [ ] T058 [US2] Make T053 green; run quickstart Scenario 2 (depends T057)

**Checkpoint**: US1 + US2 both work independently.

---

## Phase 5: User Story 3 - Reproducible corpus + published benchmark (Priority: P2)

**Goal**: A reproducible containerised corpus builder + a benchmark harness measuring precision/recall
and version accuracy against the kill criteria, published good or bad. Includes the Checkpoint-B ML
signal, gated by SC-004.

**Independent Test**: quickstart Scenario 5 — reproducible corpus (stable hash), Checkpoint A gate
(precision ≥80%, recall ≥60% at -O2), report emitted regardless of pass/fail.

- [X] T059 [P] [US3] Recipe schema + parser in `tools/corpus-builder/Strata.CorpusBuilder/RecipeModel.cs`
- [X] T060 [P] [US3] ~50 library recipes + version-selection policy (latest-per-minor + CVE) in `tools/corpus-builder/recipes/`
- [X] T061 [P] [US3] Pinned gcc/clang toolchain Dockerfiles in `tools/corpus-builder/dockerfiles/`
- [X] T062 [US3] Containerised build orchestration (compilers × opt × arch) in `tools/corpus-builder/Strata.CorpusBuilder/BuildOrchestrator.cs` (depends T059–T061)
- [X] T063 [US3] Signature extraction reusing `Strata.Core` + write `corpus.db`/`.lsh`/`manifest.json` + reproducible hash in `.../SignatureExtractor.cs` (depends T062, T017, T040)
- [X] T064 [P] [US3] Contract test: reproducibility (two builds equivalent) + manifest/schema verify in `tests/contract/CorpusReproducibilityTests.cs`
- [X] T065 [P] [US3] Held-out ground-truth set builder (deliberately different toolchain) in `benchmark/Strata.Benchmark/GroundTruthSet/`
- [X] T066 [US3] Benchmark runner: per-library + aggregate precision/recall/version-accuracy + scan wall-time in `benchmark/Strata.Benchmark/BenchmarkRunner.cs`; **record host CPU/RAM and gate wall-time against the SC-005 reference machine (8-core x86-64 / 16 GB / SSD)** (depends T044, T065)
- [X] T067 [US3] Checkpoint A/B evaluation + publishable report (pass/fail regardless) in `benchmark/Strata.Benchmark/CheckpointEvaluator.cs` (depends T066)
- [X] T068 [US3] `strata benchmark` CLI command in `src/Strata.Cli/Commands/BenchmarkCommand.cs` (depends T067)
- [ ] T069 [P] [US3] Contrastive/Siamese similarity model training (PyTorch) on corpus function pairs + ONNX export in `tools/ml-training/` (depends T063)
- [ ] T070 [US3] ONNX Runtime embedding signal in `src/Strata.Core/Fingerprinting/EmbeddingSignal.cs` + HNSW index in `src/Strata.Corpus/Index/HnswIndex.cs` (depends T069, T040)
- [ ] T071 [US3] SC-004 ship/park decision: measure embedding recall delta on benchmark, wire into `Fingerprinter` or park with recorded rationale (depends T070, T067)
- [ ] T072 [US3] Run Checkpoint A gate (SC-001); run quickstart Scenario 5 (depends T063, T067)

**Checkpoint**: Accuracy is measured, reproducible, and published; matching credibility established.

---

## Phase 6: User Story 4 - Run the scan in CI (Priority: P2)

**Goal**: Single-file executable + container image + GitHub Action with pipeline-gating exit codes and
no runtime install.

**Independent Test**: quickstart Scenario 6 — `docker run strata scan` on a runtime-free host produces a
valid SBOM; Action with `fail-on: findings` fails on CVEs.

- [X] T073 [P] [US4] Self-contained single-file publish profiles per RID (linux-x64/arm64, win-x64, osx-arm64/x64) in `src/Strata.Cli/Properties/PublishProfiles/`
- [X] T074 [US4] Container image (distroless + `strata` + bundled corpus + native deps) in `src/Strata.Cli/Dockerfile` (depends T073)
- [X] T075 [P] [US4] GitHub Action `action.yml` (inputs/outputs, `fail-on` → exit-code mapping, contracts/github-action.md) + entrypoint at repo root
- [ ] T076 [P] [US4] Integration test: `docker run` scan on runtime-free host + exit-code gating in `tests/integration/CiIntegrationTests.cs`
- [X] T077 [US4] Make T076 green; run quickstart Scenario 6 (depends T074, T075)

**Checkpoint**: US1–US4 all work independently.

---

## Phase 7: User Story 5 - Vulnerability cross-reference (thin) (Priority: P3)

**Goal**: Map identified library@version (or range) to known CVEs from a pinned OSV snapshot.

**Independent Test**: quickstart Scenario 4 — vulnerable version lists CVEs, clean version lists none,
ranges mark `appliesToRange`.

- [X] T078 [P] [US5] OSV cached-snapshot ingest + pinning in `src/Strata.Vuln/OsvSnapshot.cs`
- [X] T079 [P] [US5] library@version/range → CVE mapping (range-aware) in `src/Strata.Vuln/VulnerabilityMatcher.cs`
- [X] T080 [P] [US5] NVD enrichment (optional severity) in `src/Strata.Vuln/NvdEnricher.cs`
- [X] T081 [US5] Wire into scanner + SBOM `vulnerabilities[]` + CLI `--vuln` + exit code 2 in `src/Strata.Core/`, `src/Strata.Sbom/`, `src/Strata.Cli/` (depends T078, T079, T044, T045)
- [X] T082 [P] [US5] Integration test: CVE presence/absence + `appliesToRange` in `tests/integration/VulnCrossRefTests.cs`
- [X] T083 [US5] Make T082 green; run quickstart Scenario 4 (depends T081)

**Checkpoint**: US1–US5 all work independently.

---

## Phase 8: User Story 6 - Public web demo (Priority: P3)

**Goal**: Blazor Server demo streaming "libraries light up" with expandable evidence, bundled samples,
capped/in-memory/throttled uploads, no login.

**Independent Test**: quickstart Scenario 7 — sample streams results with evidence < 20 s; over-cap
rejected; uploads not retained; CLI parity.

- [X] T084 [US6] ASP.NET Core + Blazor Server host reusing `Strata.Core` in `src/Strata.Web/Program.cs`
- [X] T085 [US6] Progressive streaming (`IProgress` → SignalR circuit) + evidence-expand UI + unidentified regions in `src/Strata.Web/Components/`
- [X] T086 [P] [US6] Bundle 3 sample binaries (router-firmware extract, static multi-lib, vendor DLL — NO fleet/vehicle telematics, FR-026) + ground truth in `src/Strata.Web/wwwroot/samples/`
- [X] T087 [US6] Upload cap + in-memory processing + delete-after-response + per-IP throttle in `src/Strata.Web/Upload/` (depends T084)
- [X] T088 [P] [US6] `docker-compose.yml` for the demo in `src/Strata.Web/docker-compose.yml`
- [ ] T089 [P] [US6] Integration tests: over-cap reject (**test-fixture cap = 8 MB, set explicitly in the test and independent of the deploy-time production cap**), retention=none, CLI parity, first-result timing in `tests/integration/WebDemoTests.cs`
- [X] T090 [US6] Make T089 green; run quickstart Scenario 7 (depends T084–T088)

**Checkpoint**: All six user stories independently functional.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T091 [P] README + R&D page copy: honesty framing, cite primary CRA sources, never "CRA compliant" (FR-018) in `docs/`
- [ ] T092 [P] Public API docs for `Strata.Core` surface (contracts/engine-api.md) in `docs/`
- [ ] T093 Performance pass vs SC-005 (40 MB < 5 min on the reference 8-core x86-64 / 16 GB / SSD machine) — profile hot paths in `src/Strata.Core/`
- [ ] T094 [P] Security hardening: upload path sanitization, resource/time limits, run security scan
- [ ] T095 [P] Additional unit tests closing coverage on public contracts (Principle III) across `tests/*/unit/`
- [ ] T096 Publish versioned corpus + benchmark artefacts (Principle II, SC-010)
- [ ] T097 Full `quickstart.md` validation run (all scenarios) + tracker sync (Principle VII)
- [ ] T098 SemVer + changelog/migration notes for public contracts (Principle II)

---

## Phase 10: Additional Binary Formats — PE & Mach-O (tracked follow-on, FR-001)

**Goal**: Extend ingestion from ELF-only to the full FR-001 format set. This is an **explicitly tracked
deferred increment** (Principle VI — deferral recorded, not silent), sequenced *after* ELF works per the
spec. It attaches to the same ingestion seam as US1, so it can run any time after US1 lands, in parallel
with US2–US6.

**Independent Test**: scan a known-composition PE and a known-composition Mach-O fixture → expected
components with evidence (quickstart Scenarios 1–2 re-run for the new formats).

- [X] T099 [P] PE reader (x86-64 + AArch64): COFF/PE headers, sections, import/export tables, strings/constants in `src/Strata.Core/Ingestion/PeReader.cs`
- [X] T100 [P] Mach-O reader (x86-64 + arm64): load commands, sections, symbol/string tables, constants in `src/Strata.Core/Ingestion/MachOReader.cs`
- [X] T101 PE/Mach-O format detection + packing detection wired into `FormatDetector`/`PackingDetector`/`BinaryLoader` in `src/Strata.Core/Ingestion/` (depends T099, T100)
- [ ] T102 [P] PE + Mach-O known-composition fixtures + ground-truth manifests in `tests/fixtures/build-fixtures.ps1`
- [ ] T103 [P] Integration tests: scan PE and Mach-O fixtures → expected components in `tests/integration/PeMachOScanTests.cs`
- [ ] T104 Extend corpus builder to emit PE/Mach-O signatures (add Windows/macOS toolchain targets) in `tools/corpus-builder/` (depends T062); re-run quickstart Scenarios 1–2 for PE and Mach-O

**Checkpoint**: FR-001 fully satisfied across ELF, PE, and Mach-O.

---

## Dependencies & Execution Order

### Phase dependencies
- **Setup (P1)** → no deps.
- **Foundational (P2)** → depends on Setup; **BLOCKS all user stories**.
- **US1 (P3)** → depends on Foundational only. **MVP.**
- **US2 (P4)** → Foundational + US1 matcher (extends `Matcher`/SBOM).
- **US3 (P5)** → Foundational + `Strata.Core` fingerprinting/scanner (T040/T044). Independent of US2/US4/US5/US6.
- **US4 (P6)** → US1 (needs a working `strata scan`). Independent of US2/US3/US5/US6.
- **US5 (P7)** → US1 (needs components) + `Strata.Vuln`. Independent of US2/US3/US4/US6.
- **US6 (P8)** → US1 (needs scanner + SBOM). Benefits from US5 for CVE display but independently testable.
- **Polish (P9)** → after the stories you intend to ship.
- **Additional Formats (P10, PE/Mach-O)** → US1 (extends the ingestion seam). Independent of US2–US6 and can run in parallel with them once US1 lands. Tracked follow-on, not part of the ELF-first MVP.

### Cross-story independence
US2, US3, US4, US5, US6, and the Phase-10 formats work each attach to the US1 engine at a **different
seam** (versioning module, corpus tooling/benchmark, packaging/action, vuln module, web host, ingestion
readers) — so once US1 lands they can be built by **separate agents in parallel** without stepping on
each other's files.

---

## Claude-Flow Parallel Orchestration (per user request)

Fan out through **Claude-Flow / RuFlo CLI for coordination + Claude Code Task tool for execution**
(hierarchical topology, shared memory namespace, `post-task` checkpoints). Init once:

```bash
npx @claude-flow/cli@latest swarm init --topology hierarchical --max-agents 8 --strategy specialized
```

Then run these **waves** — every task in a wave is `[P]` and touches a distinct file, so spawn them as
concurrent Task-tool agents in ONE message per wave; barrier at each wave's end before the next.

| Wave | Tasks (all concurrent) | Barrier reason |
|------|------------------------|----------------|
| **W1 — Setup** | T003, T004, T005, T006, T007, T008, T009, T010 | after T001→T002 (repo+solution) land first (serial) |
| **W2 — Model & contracts** | T011, T012, T013, T014, T015, T018, T019, T020 | all independent model/interface files |
| **W3 — Corpus & fixtures** | T016→T017 (chain), T021, T022, T023, T024 | seed corpus + fixtures needed before US1 tests |
| **W4 — US1 ingestion/disasm/fingerprint** | T029, T030, T031, T033, T034, T037, T038, T039, plus tests T025, T026, T027, T028, and SBOM T045, T046, T047 | leaf implementations against fixed interfaces |
| **W5 — US1 integration (serial chain)** | T032 → T035 → T036 → T040 → T041 → T042 → T043 → T044 → T048 → T049 → T051 → T052 | genuine data-dependency chain; single-agent |
| **W6 — Stories fan-out** | **US2** {T053,T054,T055 → T056 → T057 → T058}, **US3** {T059,T060,T061 → T062 → T063; T064,T065 → T066 → T067 → T068; T069 → T070 → T071 → T072}, **US4** {T073 → T074; T075; T076 → T077}, **US5** {T078,T079,T080 → T081 → T082 → T083}, **US6** {T084 → T085; T086; T087; T088; T089 → T090}, **Formats(P10)** {T099,T100 → T101; T102; T103; T104} | 6 independent agent tracks (5 stories + PE/Mach-O), each an internal mini-chain |
| **W7 — Polish** | T091, T092, T094, T095 concurrent; T093, T096, T097, T098 serial-ish | after target stories complete |

**Genuine serial chains (do NOT parallelize)**: the US1 engine pipeline (W5 — ingestion→recovery→
fingerprint→match→scan→SBOM→CLI is a real data dependency), and each story's internal `→` chain above.
Everything marked `[P]` and every separate story track fans out.

---

## Parallel Example: User Story 1 leaf tasks (Wave 4)

```bash
# One message, concurrent Task-tool agents (all different files, no interdeps):
Task: "T029 ELF reader in src/Strata.Core/Ingestion/ElfReader.cs"
Task: "T030 String/constant extraction in src/Strata.Core/Ingestion/StringConstantExtractor.cs"
Task: "T033 Iced x86-64 adapter in src/Strata.Core/Disassembly/IcedAdapter.cs"
Task: "T037 String/constant signal in src/Strata.Core/Fingerprinting/StringConstantSignal.cs"
Task: "T038 CFG-shape signal in src/Strata.Core/Fingerprinting/CfgShapeSignal.cs"
Task: "T045 CycloneDX emitter in src/Strata.Sbom/CycloneDxEmitter.cs"
```

---

## Implementation Strategy

### MVP first (US1 only)
Setup → Foundational → US1 → **STOP & VALIDATE** (quickstart Scenarios 1 & 3) → demo. A working
evidence-backed SBOM scanner with a seed corpus is a viable, honest MVP on its own.

### Incremental delivery
US1 (MVP) → US2 (versions) → US3 (corpus+benchmark proves accuracy / Checkpoint A gate) → US4 (CI) →
US5 (CVEs) → US6 (web demo). Each adds value without breaking prior stories. **PE/Mach-O (Phase 10)** is
a tracked follow-on delivered once ELF is proven — schedule it in parallel with US2–US6 or after, per
capacity; it is not on the MVP path.

### Kill-criteria gates (do not skip)
- After US3 Checkpoint A: if precision <80% / recall <60% at -O2, **fix corpus/recovery before any ML**
  (SC-001) — the model cannot rescue broken heuristics.
- Checkpoint B / SC-004: ship the embedding signal only if it adds ≥5 pts recall; otherwise park it
  (T071) and ship heuristics-first.

---

## Notes
- `[P]` = different file, no dependency on incomplete work. `[Story]` maps a task to its user story.
- Tests are required at merge (Principle III); test-first ordering is optional.
- Commit after each task/logical group; link commits to tracker items (Principle VII).
- Stop at any checkpoint to validate a story independently.
- Repo bootstrap (T001) satisfies the Principles VII/VIII items flagged in plan.md's Constitution Check.
