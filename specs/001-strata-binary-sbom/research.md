# Phase 0 Research — Strata Binary-to-SBOM

Each decision below resolves a Technical-Context choice or a spec Assumption/open-question. Decisions
are interdependent (the C#/single-binary constraint cascades through all of them), so they are
synthesised as one coherent architecture rather than researched in isolation. Format per decision:
**Decision · Rationale · Alternatives considered**.

---

## R1. Implementation language & packaging — .NET 10 (C#), self-contained single-file

- **Decision**: Build the engine, CLI, web demo, and benchmark in **C# on .NET 10**. Ship the CLI as a
  **self-contained, single-file** executable per RID (linux-x64, linux-arm64, win-x64, osx-arm64/x64)
  plus a container image. Trim where safe; use NativeAOT for the CLI where the dependency set allows,
  otherwise self-contained single-file (both satisfy "no separate runtime install").
- **Rationale**: The author's default is C# and a single-binary CLI that runs without a Python install
  is a stated hard constraint (spec Constraints; brainstorm §66). .NET's self-contained single-file /
  NativeAOT publish delivers exactly that. One language spans engine + CLI + web + benchmark, keeping
  the engine reused everywhere (Principle I).
- **Alternatives considered**: **Rust** (excellent for binary analysis, best-in-class single-binary) —
  rejected as it fights the author-fluency constraint; **Python** (richest binary-analysis ecosystem:
  angr, LIEF, capstone) — rejected for the user-facing tool because it violates the no-runtime
  constraint; kept only for off-ship ML training. **Go** — viable single-binary but weaker for the
  author and no advantage over .NET here.

## R2. Instruction decoding / disassembly — Iced (x86-64), Capstone (AArch64), optional headless plug-in

- **Decision**: Decode x86-64 with **Iced** (pure-C#, no native dependency, exact, fast). Decode
  AArch64 with **Capstone** via a thin binding, bundled as a native lib in the self-contained build.
  Function-boundary recovery and CFG construction are **first-party** (the fast path). A headless
  disassembler (**Ghidra headless** or **radare2/r2pipe**) is an **optional plug-in** for hard cases,
  never a hard dependency.
- **Rationale**: Resolves spec Open Question 1. Iced keeps x86-64 — the primary target — free of native
  deps, aiding NativeAOT and reproducibility. Capstone is the standard multi-arch decoder for AArch64.
  Keeping recovery first-party is the differentiator and keeps the fast path dependency-light; the
  optional plug-in buys depth without forcing a 1 GB Ghidra install on every CI user.
- **Alternatives considered**: **Capstone for both arches** — rejected for x86-64 because Iced is more
  accurate/faster and native-dep-free. **Mandatory Ghidra** — rejected: too heavy for CI and against the
  single-binary goal. **B2R2** (.NET binary-analysis framework) — evaluated as a reference; may be
  borrowed for CFG utilities but not adopted wholesale to keep the engine first-party and lean.

## R3. Binary format parsing — first-party thin readers, ELFSharp/LibObjectFile as reference

- **Decision**: Implement **first-party** ELF/PE/Mach-O readers in `Strata.Core.Ingestion`, reading only
  what Strata needs (arch, sections, entry points, symbol/string tables, embedded constants, packing
  indicators). Keep `ELFSharp` and `LibObjectFile` as reference/fallback and for cross-checking in tests.
- **Rationale**: The parsing surface we need is small and stable; a first-party reader avoids a heavy
  dependency, keeps determinism under our control, and lets us treat *stripped* as the default case
  (FR-002) rather than fighting a library's symbol-centric assumptions. ELF first (x86-64 then AArch64),
  PE and Mach-O after (spec sequencing).
- **Alternatives considered**: **LIEF** (superb multi-format) — native/Python-leaning, rejected for the
  ship path. **Full reliance on ELFSharp** — fine for ELF but no unified PE/Mach-O story and less control
  over packing detection.

## R4. Function-boundary recovery — layered heuristics with confidence

- **Decision**: Recover boundaries with layered signals: (1) any present symbols/exports, (2) call-target
  analysis from the disassembled call graph, (3) prologue/epilogue pattern heuristics per arch/ABI,
  (4) linear-sweep + recursive-descent reconciliation with gap analysis. Emit a per-function recovery
  confidence used downstream. Optional Ghidra/r2 plug-in supplies boundaries for regions the heuristics
  score low.
- **Rationale**: Stripped static binaries (FR-002/006) have no symbol table, so recovery must be robust
  without them; layering cheap-to-expensive signals matches the "cheapest first" philosophy of the
  fingerprinting pipeline and yields graceful degradation with an honest confidence.
- **Alternatives considered**: **Prologue-only** — brittle across compilers/opt levels. **Disassembler-only**
  (always Ghidra) — accurate but violates R2's lightweight fast path.

## R5. Multi-signal fingerprinting — cheapest-first cascade

- **Decision**: Per function compute, cheapest-first: **(a) string/constant references** (crypto S-boxes,
  CRC/lookup tables, magic numbers, error/version strings), **(b) CFG-shape hash** (basic-block count,
  edge structure, loop-nesting summary) robust to register allocation, **(c) normalised
  instruction-sequence hash** (opcode mnemonics with operands abstracted; n-gram + MinHash), and
  optionally **(d) a learned embedding** (R7). Aggregate function hits into library-level evidence.
- **Rationale**: Directly implements FR-007. Cheap deterministic signals (a–c) must clear Checkpoint A
  (SC-001) before ML is considered — this is the Principle V phasing. Each signal targets a different
  invariance (a: content, b: structure, c: instruction selection), so they fail independently and their
  agreement is meaningful evidence (Principle XII).
- **Alternatives considered**: **Single whole-binary hash** — useless against static linking/partial
  inclusion. **Pure string matching** (à la simple strings-grep) — high recall, low precision, no version
  power; kept only as one signal. **Only ML embeddings** — opaque, unbootstrappable before a corpus exists,
  and against the honesty/evidence requirement.

## R6. Fuzzy heuristic matching & indexing — MinHash-LSH + SQLite

- **Decision**: Store exact signals (string/constant sets, CFG-shape hashes) in **SQLite** with covering
  indexes; index normalised-instruction n-grams as **MinHash signatures in an LSH** structure for
  sub-linear fuzzy lookup. Persist the LSH bands alongside the SQLite file as part of the versioned
  signature DB.
- **Rationale**: SQLite ships inside the CLI (single-file, zero-config, deterministic) and handles the
  structured store; MinHash-LSH gives approximate-nearest-neighbour recall over instruction n-grams
  within the <5 min budget (SC-005) without loading the whole corpus.
- **Alternatives considered**: **Postgres/served DB** — violates single-binary/offline. **Full pairwise
  comparison** — O(target×corpus), blows the time budget. **Elastic/vector DB service** — external
  dependency, against the demo's offline/self-contained posture.

## R7. Learned similarity signal — Siamese/contrastive model, ONNX inference, trained in Python

- **Decision**: Train a **contrastive/Siamese function-similarity model** on corpus function pairs
  (same function across compiler/opt variants = positive; different = negative) in **PyTorch**
  (`tools/ml-training`), export to **ONNX**, and run inference in-process via **ONNX Runtime** bundled in
  the self-contained build. Index embeddings with **HNSW** for ANN search. Gate: include the signal only
  if it adds **≥5 pts recall** over heuristics on the benchmark (SC-004); otherwise ship heuristics-first
  and park the model.
- **Rationale**: Resolves Open Question 2. Contrastive learning across compiler/opt variants is the
  established approach (Asm2Vec/SAFE/jTrans lineage) to the cross-compiler fuzziness heuristics miss; ONNX
  keeps training (Python) off the ship path while inference stays in-process and runtime-free for the user.
  Minimum-viable corpus for training ≈ the initial ~50-library matrix (thousands of function-variant pairs);
  the benchmark decides worth before it ships.
- **Alternatives considered**: **Adopt a pre-trained public model + fine-tune** — kept as a fast-start
  option; risk is licence/opset portability to ONNX and domain fit. **Train from scratch, ship-blocking** —
  rejected; heuristics-first must stand alone (Checkpoint A) so ML is never on the critical path.

## R8. Version resolution — present/absent function sets + version-specific evidence, honest bounding

- **Decision**: Resolve version by intersecting (1) the set of library functions present/absent in the
  target against per-version corpus signatures, (2) version-specific strings/constants (e.g. embedded
  version banners, changed lookup tables). Output the **tightest range consistent with all evidence**;
  collapse to an exact version only when evidence is unambiguous. Never report narrower than the evidence
  (FR-013, SC-003).
- **Rationale**: Implements the honesty-over-precision requirement. Function-set diffing across versions is
  what distinguishes minor releases when strings are absent; combining both signals bounds the version
  without over-claiming.
- **Alternatives considered**: **Version strings only** — often stripped/absent in embedded builds.
  **Nearest-single-version guess** — violates FR-013's "never more precise than evidence".

## R9. Confidence scoring — distinctiveness-weighted coverage, benchmark-tunable

- **Decision**: Library-level confidence = a function of (i) **coverage** (fraction of the library's
  fingerprintable functions matched) and (ii) **distinctiveness** (down-weight functions whose signatures
  also appear in other corpus libraries — shared/inlined utility code). Start with a transparent weighted
  formula; make the combiner **pluggable** so the benchmark can compare formula vs learned combiner
  (SC-002/003). Every score is reproducible and attached to its evidence (Principle XII).
- **Rationale**: Resolves Open Question 4. Distinctiveness weighting prevents ubiquitous helper functions
  (memcpy-alikes, CRC) from inflating confidence; coverage prevents a single incidental match from
  asserting presence. A transparent formula first keeps the number explainable; the harness measures
  whether a learned combiner beats it before adopting opacity.
- **Alternatives considered**: **Raw match count** — dominated by common code, uninterpretable.
  **Learned combiner from day one** — opaque and unbootstrapped; deferred behind measurement.

## R10. Signature-DB format & versioning — publishable, reproducible artefact

- **Decision**: Ship the corpus as a **versioned artefact**: `strata-corpus-<version>.db` (SQLite) +
  sidecar LSH/HNSW index + a signed `manifest.json` (corpus version, library list with versions, build
  toolchain hashes, schema version). Reproducible build ⇒ equivalent DB (SC-009). Consumers pin a corpus
  version (Principle II); the DB schema version is a public contract.
- **Rationale**: Implements FR-009/010 and Reproducibility (SC-009). A manifest with toolchain hashes makes
  "reproducible" verifiable and the corpus publishable/citable in its own right (brainstorm goal).
- **Alternatives considered**: **Bundle corpus into the executable** — bloats the binary and couples corpus
  updates to tool releases; rejected. Corpus is a separately versioned, downloadable artefact the CLI
  resolves/pins.

## R11. Corpus build farm — containerised, reproducible, pinned toolchains

- **Decision**: `tools/corpus-builder` orchestrates **Docker** builds: pinned gcc and clang toolchain
  images, per-library **recipes** (source URL+hash, build flags), matrixed over opt levels (-O0/-O2/-O3/-Os)
  and target arches, then runs `Strata.Core` signature extraction on the outputs. **Version-selection
  policy: latest per minor line + versions associated with known CVEs** (spec Assumption; Open Question 3).
  Builds are reproducible (pinned base images, fixed flags, no timestamps in signatures).
- **Rationale**: Implements FR-009/010/011 and SC-009. Containers give reproducibility and parallelism;
  the CVE+latest-per-minor policy keeps build volume tractable while covering compliance-relevant versions.
  Corpus builds are **ephemeral CI** (Principle X exempt) — fully automatable.
- **Alternatives considered**: **Every historical release** — combinatorial explosion, little marginal
  recall. **Prebuilt distro packages** — inconsistent flags/toolchains, not reproducible, defeats the
  cross-compiler/opt matrix the benchmark needs.

## R12. SBOM emission — CycloneDX lib + first-party SPDX, evidence via standard extension points

- **Decision**: Emit **CycloneDX 1.6 JSON** using the official `CycloneDX` .NET library, attaching evidence
  via CycloneDX's `evidence`/`properties` fields and confidence as a scored property. Emit **SPDX 2.3**
  (tag-value + JSON) via a first-party emitter. Both include the "unidentified regions" section as
  structured output (FR-015/016/017). SBOM documents are deterministic; `serialNumber` and `timestamp`
  are opt-in/pinnable for golden tests (Principle IV).
- **Rationale**: CycloneDX has richer native support for evidence/occurrences, matching Strata's
  evidence-first design; a thin first-party SPDX emitter avoids depending on a heavier/immature SPDX .NET
  library while meeting the dual-format requirement.
- **Alternatives considered**: **SPDX-only** — weaker evidence model. **Hand-rolled JSON for both** —
  reinvents CycloneDX validation; rejected.

## R13. Vulnerability cross-reference — OSV primary, cached snapshot, range-aware

- **Decision**: Map library@version (or range) to CVEs via **OSV** (primary; clean ecosystem→vuln ranges),
  enriched by **NVD** where useful. Query against a **cached, pinned snapshot** for determinism and offline
  CI; refresh is an explicit corpus-adjacent step. For range-resolved libraries, report vulns applicable to
  any version in the range (FR-019, US5 scenario 2). Kept deliberately thin — no reachability/triage.
- **Rationale**: OSV's structured affected-ranges fit range-aware reporting; a cached snapshot keeps scans
  deterministic (Principle IV) and CI offline-capable. Deep triage is OSPulse's job (non-goal).
- **Alternatives considered**: **Live NVD API at scan time** — non-deterministic, rate-limited, breaks
  offline CI. **Bundling a full vuln DB in the CLI** — heavy and stale; cached snapshot is pinned+refreshable.

## R14. Web demo — ASP.NET Core + Blazor Server (SignalR), in-memory, capped, throttled

- **Decision**: Build the demo as **ASP.NET Core + Blazor Server**; stream progressive results
  ("libraries light up") over the built-in SignalR circuit as `Strata.Core` reports matches. Bundle
  pre-loaded **sample binaries** (router-firmware extract, stripped static multi-lib hello-world,
  vendor-style DLL — **no fleet/vehicle telematics**, FR-026). Enforce an **upload size cap**, process
  **in memory then delete** (FR-025), and throttle by IP/session for abuse control without login.
- **Rationale**: Reuses the engine directly (Principle I) with no separate API contract to maintain;
  Blazor Server's push model natively fits the streaming reveal and the <20 s first-result goal (SC-006).
  Resolves Open Questions 5b (retention: none) and 6 (hosting/cap/abuse) with concrete defaults; exact host,
  cap value, and throttle limits are ops parameters set at deploy.
- **Alternatives considered**: **SPA + REST/WebSocket API** — a second contract and CORS/hosting surface for
  no user benefit at demo scale. **Serverless functions** — cold starts fight the 20 s first-result and
  large-upload handling.

## R15. Benchmark harness — held-out set, kill-criteria evaluation

- **Decision**: `benchmark/Strata.Benchmark` scans a **held-out ground-truth set** (binaries built with
  compiler versions/flags deliberately different from the corpus, FR-022), computes **top-1 precision,
  recall, and version-resolution accuracy** per library and in aggregate, and evaluates against
  **Checkpoint A** (SC-001) and **Checkpoint B** (SC-002/003) thresholds, emitting a publishable report
  regardless of outcome (SC-010). Also measures scan wall-time against SC-005.
- **Rationale**: Implements FR-022/023 and operationalises Principle XI — viability is decided from measured
  data. The held-out/different-toolchain rule prevents corpus-overfit metrics.
- **Alternatives considered**: **Test on corpus-adjacent builds** — inflated, dishonest metrics.
  **Manual spot-checks** — not reproducible, can't gate the kill criteria.

---

## Open questions — final disposition

| Spec open question | Disposition |
|--------------------|-------------|
| 1. Function recovery: first-party vs disassembler | **First-party fast path + optional Ghidra/r2 plug-in** (R2, R4) |
| 2. Embedding model approach & min corpus | **Contrastive/Siamese, PyTorch→ONNX, gated by SC-004; ~50-lib matrix minimum** (R7) |
| 3. Corpus version scope | **Latest per minor + CVE-affected versions** (R11) |
| 4. Confidence scoring | **Distinctiveness-weighted coverage, pluggable/benchmark-tunable** (R9) |
| 5a/5c. Licence & corpus publication | **Apache-2.0 code (resolved in spec); corpus under compatible permissive/data licence** (R10) |
| 5b. Web demo retention | **None — in-memory, deleted after response** (R14) |
| 6. Demo hosting / cap / abuse | **Fortitude R&D host, fixed upload cap + throttle, no login; values set at deploy** (R14) |

All open technical questions above are resolved; the decisions feed directly into the plan.
