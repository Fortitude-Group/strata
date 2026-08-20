# Feature Specification: Strata — Binary-to-SBOM

**Feature Branch**: `001-strata-binary-sbom`

**Created**: 2026-08-19

**Status**: Draft

**Scope**: A tool that takes a compiled, stripped, possibly statically-linked native binary with no source and no debug info, and produces a CycloneDX/SPDX SBOM of the open-source libraries and versions compiled into it, each with a confidence score and evidence.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Produce an evidence-backed SBOM from a stripped native binary (Priority: P1)

A firmware engineer at a mid-sized hardware company is handed a 40 MB vendor-supplied, stripped, statically-linked native binary and a compliance ticket. They cannot obtain source. They run a single command against the binary and receive a machine-readable SBOM listing the open-source libraries compiled into it. Every listed library carries a confidence score and the specific evidence that supports it (matched functions, strings, constants), and the report explicitly names the regions of code it could **not** identify.

**Why this priority**: This is the whole reason the tool exists. Existing SBOM generators return almost nothing against a stripped static binary; this story delivers the core value — a defensible, evidence-backed inventory of what is inside software the user did not build — and is a viable product on its own even before version sharpening, CI wrappers, CVE mapping, or the web demo exist.

**Independent Test**: Run the tool against a stripped binary of known composition (built for test) and confirm it emits a valid CycloneDX JSON and SPDX document listing the expected libraries, each with a confidence score and attached evidence, plus a section reporting unidentified code regions. Fully testable with only the scanner, a reference corpus, and one sample binary.

**Acceptance Scenarios**:

1. **Given** a stripped, statically-linked ELF (x86-64) that was compiled against several known open-source libraries, **When** the engineer runs a scan requesting CycloneDX output, **Then** the tool produces a valid CycloneDX JSON document that lists each identified library as a component with a confidence score and an evidence record, and produces an equivalent SPDX document on request.
2. **Given** the same binary, **When** the scan completes, **Then** the output includes a human-readable report and a machine-readable report, both containing a clearly delineated "low confidence / unidentified code regions" section.
3. **Given** a binary that contains code the tool cannot attribute to any library in the corpus, **When** the scan completes, **Then** that code is reported as unidentified rather than being guessed at or silently omitted, and no component is reported without at least one piece of supporting evidence.
4. **Given** a binary in an accepted native format, **When** it is scanned, **Then** the tool reports the detected architecture and, where present, imported/exported symbols, string tables, and embedded constants used as evidence.
5. **Given** a packed or obfuscated binary, **When** it is scanned, **Then** the tool detects and flags the packing and reports that it did not attempt to unpack, rather than returning misleading results.

---

### User Story 2 - Resolve library versions with honest precision (Priority: P2)

The engineer needs to know not just *which* libraries are present but *which versions*, because the compliance and vulnerability questions turn on the version. The tool resolves the version of each identified library as precisely as the evidence allows — an exact version where the evidence is unambiguous, a bounded range (e.g. "1.2.8–1.2.11") where it is not — and never reports a version more precisely than the evidence supports.

**Why this priority**: Version is what makes an SBOM actionable for compliance and vulnerability work, and honest version bounding is a core differentiator against tools that assert false precision. It builds directly on Story 1's identification and is the natural next increment.

**Independent Test**: Scan binaries built against known library versions (including versions differing only at the patch/minor level) and confirm the reported version or range contains the true version, tightens to an exact version when version-distinguishing evidence is present, and widens to a range when it is not — never excluding the true version and never narrower than the evidence justifies.

**Acceptance Scenarios**:

1. **Given** a binary linked against a library version that carries version-distinguishing evidence (version strings, version-specific constants, or a distinctive set of present/absent functions), **When** it is scanned, **Then** the tool reports the exact version with the supporting evidence.
2. **Given** a binary linked against a version that cannot be pinned exactly from available evidence, **When** it is scanned, **Then** the tool reports a bounded version range that contains the true version, and the reported range is no narrower than the evidence supports.
3. **Given** any identified library, **When** its version is reported, **Then** the report never states a version more precise than the evidence justifies, and the basis for the version claim is recorded as evidence.

---

### User Story 3 - Prove the tool's accuracy with a reproducible corpus and published benchmark (Priority: P2)

To be credible, the tool must be able to state how accurate it is on binaries it has never seen. A reproducible build process compiles a curated set of open-source libraries across versions, compilers, and optimisation levels into a versioned, publishable signature corpus. A separate benchmark harness scans a held-out set of binaries with known ground truth — deliberately built with different compiler versions and flags than the corpus — and measures identification precision/recall and version-resolution accuracy against defined kill criteria. Results are published, good or bad.

**Why this priority**: Without measured accuracy the tool's claims are unfounded, and the kill criteria decide whether the approach is even viable before further investment. The corpus is also a publishable artefact in its own right. This is essential infrastructure that gates Stories 1 and 2's credibility, so it ships alongside the MVP rather than after it.

**Independent Test**: Rebuild the corpus from scratch reproducibly and confirm it produces the same signatures; run the benchmark harness against the held-out set and confirm it emits precision, recall, and version-resolution metrics per library and in aggregate, and reports pass/fail against each kill-criteria checkpoint.

**Acceptance Scenarios**:

1. **Given** the curated library list, **When** the corpus build is run, **Then** it compiles each library across the defined versions, compilers, and optimisation levels, extracts signatures, and stores them in an indexed, versioned signature database, reproducibly (a rebuild yields equivalent signatures).
2. **Given** a held-out benchmark set with known ground truth built with compiler versions/flags not used for the corpus, **When** the benchmark harness runs, **Then** it reports top-1 library-identification precision and recall and version-resolution accuracy, per library and in aggregate.
3. **Given** the benchmark results, **When** they are evaluated against the kill criteria, **Then** the harness reports whether each checkpoint's thresholds are met, and the results are published regardless of outcome.

---

### User Story 4 - Run the scan in CI so it re-runs on every new blob (Priority: P2)

The vendor periodically sends a new binary. The engineer wires the scan into their pipeline so that each new blob is scanned automatically, the SBOM is regenerated, and the pipeline signals success or failure through standard exit codes and a reusable pipeline step, without any interactive tooling or a local language runtime install.

**Why this priority**: CI-friendliness is a stated core differentiator against expensive, closed, non-CI-shaped commercial tools. It turns a one-off scan into an ongoing control. It depends on Story 1 existing but is otherwise independent.

**Independent Test**: Invoke the scan non-interactively in a pipeline-like environment against a sample binary and confirm it produces the SBOM artefacts and returns exit codes suitable for gating a pipeline; run it via the provided pipeline step and via the provided container image and confirm equivalent results with no separate language-runtime prerequisite.

**Acceptance Scenarios**:

1. **Given** a pipeline environment, **When** the scan is invoked as a single non-interactive command against a binary, **Then** it produces the SBOM artefacts and returns an exit code that distinguishes success, "completed with findings needing attention", and error.
2. **Given** a hosted CI system, **When** the engineer adds the provided pipeline step (action wrapper) referencing a binary, **Then** the scan runs and its SBOM output is available as a pipeline artefact.
3. **Given** an environment without the tool's implementation language installed, **When** the engineer runs the provided container image (or single-file executable) against a binary, **Then** the scan completes without requiring a separate language runtime to be installed.

---

### User Story 5 - Cross-reference identified components against known vulnerabilities (Priority: P3)

Having identified library@version pairs, the engineer wants an immediate, if shallow, indication of which of them have known published vulnerabilities, so they know what to worry about first. The tool maps each identified library@version to known CVEs from public vulnerability data and includes them in the report. This is deliberately thin — identification is the value; deep triage is out of scope.

**Why this priority**: It converts the inventory into an early "what to worry about" list, materially useful to the target user, but it is a thin pass over public data and depends on Stories 1–2 producing library@version pairs first.

**Independent Test**: Feed the tool a set of identified library@version pairs including at least one with known published vulnerabilities and confirm the report lists the corresponding known vulnerabilities for that pair and lists none for a pair with no known vulnerabilities, sourced from public vulnerability data.

**Acceptance Scenarios**:

1. **Given** an identified library@version pair with known published vulnerabilities, **When** the report is generated, **Then** it lists the corresponding known vulnerabilities from public vulnerability data.
2. **Given** a library reported only as a version range, **When** vulnerabilities are cross-referenced, **Then** the report reflects the range (e.g. vulnerabilities applicable to any version in the range) rather than asserting a single version.
3. **Given** an identified pair with no known published vulnerabilities, **When** the report is generated, **Then** no vulnerabilities are asserted for it.

---

### User Story 6 - Demonstrate the tool publicly via an interactive web demo (Priority: P3)

A visitor to the Fortitude Omnis R&D page wants to see the tool work without installing anything. They open a web demo, either pick a pre-loaded sample binary or upload their own (within a size cap), and watch libraries "light up" as they are identified, with the evidence for each match expandable. Uploaded binaries are processed and then discarded, not retained.

**Why this priority**: This is the public showpiece that demonstrates technical depth and drives interest, but it depends on the core scan existing and is not needed by the primary in-CI user, so it follows the CLI and corpus/benchmark work.

**Independent Test**: Load the web demo, select a bundled sample binary, and confirm identified libraries appear progressively with expandable per-match evidence and that first results appear quickly; upload a binary within the cap and confirm it is processed and not retained, and that an over-cap upload is rejected.

**Acceptance Scenarios**:

1. **Given** the web demo with bundled sample binaries, **When** a visitor selects a sample, **Then** identified libraries appear progressively with per-match evidence that can be expanded, and first results appear quickly for the bundled samples.
2. **Given** a visitor uploading their own binary within the size cap, **When** the scan runs, **Then** the binary is processed and then deleted — not retained after the result is returned.
3. **Given** a visitor attempting to upload a binary over the size cap, **When** they submit it, **Then** the upload is rejected with a clear message and no scan is attempted.
4. **Given** the public demo with no login, **When** it is used, **Then** abuse is limited (e.g. by an upload size cap and request throttling) without requiring the visitor to create an account.

---

### Edge Cases

- **Unsupported or unrecognised format**: A file that is not a supported native binary (wrong format, truncated, corrupt header) is rejected with a clear message rather than partially processed.
- **Packed / obfuscated / anti-analysis binaries**: Detected and flagged; the tool explicitly reports it did not unpack and does not present degraded results as authoritative.
- **No identifiable content**: A binary that matches nothing in the corpus yields a valid, empty-of-components SBOM whose unidentified-regions section explains that nothing was matched — never an error framed as "no libraries present".
- **Statically-linked vs dynamically-linked**: Both are handled; a dynamically-linked binary's imported symbols are used as additional evidence, while a stripped static binary (the default target case) relies on function-level fingerprints.
- **Ambiguous match between similar libraries** (e.g. forks, vendored copies, libraries that share code): Reported with the ambiguity reflected in confidence and evidence rather than resolved by an unsupported guess.
- **Very large binary**: Scans within the stated performance envelope; a binary beyond supported limits is reported as such rather than hanging.
- **Corpus does not cover a present library**: The library's code falls into the unidentified-regions report; the tool does not misattribute it to the nearest corpus entry.
- **Two versions of the same library present**: Reported as distinct evidence sets where the evidence distinguishes them.

## Requirements *(mandatory)*

### Functional Requirements

**Ingestion & parsing**

- **FR-001**: The tool MUST accept native binary executables and libraries as input. ELF (x86-64 and
  AArch64) is delivered in this feature's ELF-first build; **PE and Mach-O are in scope as an explicitly
  tracked follow-on increment** (tasks.md Phase 10), not dropped — they extend the same ingestion seam
  once ELF works (spec sequencing; Principle VI deferral is tracked, not silent).
- **FR-002**: The tool MUST treat stripped binaries (no debug info, no symbol table) as the default, expected case, not an edge case.
- **FR-003**: The tool MUST identify, for each input, the architecture, sections, entry point(s), and — where present — imported/exported symbols, string tables, and embedded constants, and MUST make these available as evidence.
- **FR-004**: The tool MUST reject inputs that are not a supported binary format (corrupt, truncated, or unrecognised) with a clear, actionable message.
- **FR-005**: The tool MUST detect packed or obfuscated binaries, flag them, and report that it did not attempt to unpack — without attempting anti-analysis defeat.

**Function recovery & fingerprinting**

- **FR-006**: The tool MUST recover function boundaries in stripped code and build a per-function control-flow representation suitable for fingerprinting.
- **FR-007**: The tool MUST compute multiple independent fingerprint signals per function — at minimum: (a) string/constant references, (b) a control-flow-shape signal robust to register allocation, and (c) a normalised instruction-sequence signal with operands abstracted — and MAY additionally compute a learned similarity signal.
- **FR-008**: The learned similarity signal, when used, MUST ship in a form that runs inside the user-facing tool without requiring a separate language runtime to be installed (training MAY be performed separately).

**Reference corpus**

- **FR-009**: The tool MUST be backed by a reference signature corpus built by compiling a curated set of open-source libraries across multiple versions, at least two compilers, and multiple optimisation levels, extracting the same fingerprint signals used at scan time.
- **FR-010**: The corpus build MUST be automated, repeatable/reproducible, and produce a versioned, indexed signature database that is publishable as an artefact in its own right.
- **FR-011**: The initial corpus MUST cover on the order of 50 curated libraries (including common compression, crypto/TLS, image, parsing, and embedded-networking libraries), with a defined path to grow to a few hundred.

**Matching, version resolution & confidence**

- **FR-012**: The tool MUST match recovered functions against the corpus, aggregate function-level matches into library-level evidence, and produce a confidence score per identified library.
- **FR-013**: The tool MUST resolve each identified library's version to an exact version where the evidence is unambiguous and to a bounded range otherwise, and MUST NOT report a version more precisely than the evidence supports.
- **FR-014**: Every reported component MUST carry at least one concrete piece of supporting evidence (which functions matched, which strings, which constants) and a confidence score; the tool MUST NOT report a component without evidence.
- **FR-015**: The tool MUST report code regions it could not attribute to any library as a distinct "low confidence / unidentified" section — no silent guesses and no silent omissions.

**Output**

- **FR-016**: The tool MUST emit a valid CycloneDX (JSON) SBOM and a valid SPDX SBOM, each with per-component evidence and confidence attached.
- **FR-017**: The tool MUST produce both a human-readable report and a machine-readable report, each including the unidentified-regions section.
- **FR-018**: The tool MUST NOT describe its output as "CRA compliant" nor the tool as "making the user compliant"; output is evidence toward an SBOM, and this framing MUST be reflected in the reports and any user-facing copy.

**Vulnerability cross-reference (thin)**

- **FR-019**: The tool MUST map each identified library@version (or version range) to known published vulnerabilities using public vulnerability data, presenting this as a thin cross-reference and not as vulnerability triage, reachability, or exploitability analysis.

**CLI & CI integration**

- **FR-020**: The tool MUST provide a single-command interface that scans a binary and selects an output format, returning exit codes suitable for pipeline gating (distinguishing success, findings-needing-attention, and error).
- **FR-021**: The tool MUST be runnable without a separate language runtime installed by the user — via a single-file executable and/or a container image — and MUST provide a reusable hosted-CI pipeline step (action wrapper).

**Benchmark harness**

- **FR-022**: The tool MUST include a benchmark harness that scans a held-out set of binaries with known ground truth — built with compiler versions and flags deliberately different from the corpus — and measures library-identification precision/recall and version-resolution accuracy, per library and in aggregate.
- **FR-023**: The benchmark harness MUST evaluate results against the defined kill-criteria checkpoints and report pass/fail for each; benchmark results MUST be publishable regardless of outcome.

**Public web demo**

- **FR-024**: The tool MUST offer a public web demo that scans a binary and reveals identified libraries progressively with expandable per-match evidence, and that ships with pre-loaded sample binaries so the effect is visible without any upload.
- **FR-025**: The web demo MUST enforce an upload size cap, MUST process uploaded binaries without retaining them after returning results (in-memory processing, then deletion), and MUST limit abuse without requiring visitors to create accounts.
- **FR-026**: The web demo's sample binaries and public copy MUST exclude fleet/vehicle telematics content.

**Honesty & governance constraints**

- **FR-027**: The tool's overriding constraint is **honesty over coverage**: the per-component evidence
  of FR-014, the version-precision limit of FR-013, and the unidentified-regions reporting of FR-015
  MUST together let a reader independently verify *why* every claim was made and *what* the tool could
  not place. No claim is presented without its basis; no gap is hidden. (This is the cross-cutting
  principle those requirements serve, not a restatement of them.)
- **FR-028**: The project MUST be released under the **Apache-2.0** licence (permissive, with an explicit patent grant), chosen to maximise CI/commercial adoption and downstream OSPulse integration for a public R&D showpiece. The signature corpus MUST be co-published under a compatible permissive/data licence.

### Key Entities *(include if data involved)*

- **Target Binary**: The input artefact under analysis — its format, architecture, sections, entry points, and any present symbols/strings/constants.
- **Recovered Function**: A function boundary recovered from the (possibly stripped) target, with its control-flow representation; the unit that fingerprints are computed over.
- **Fingerprint / Signature**: A set of independent signals derived from a function (string/constant references, control-flow-shape signal, normalised instruction-sequence signal, optional learned-similarity signal) used to compare target functions against the corpus.
- **Reference Corpus / Signature Database**: The versioned, indexed collection of signatures extracted from curated open-source libraries built across versions × compilers × optimisation levels; a publishable artefact.
- **Identified Component**: A library attributed to the target, carrying a confidence score, a resolved version (exact or range), and its evidence set — the SBOM component.
- **Evidence Record**: The concrete basis for a component or version claim (matched functions, matched strings, matched constants), attached to each component.
- **Unidentified Region**: A portion of the target's code not attributed to any corpus library, reported for honesty.
- **SBOM Document**: The output inventory in CycloneDX (JSON) and SPDX, plus the human- and machine-readable reports.
- **Vulnerability Reference**: A known published vulnerability associated with an identified library@version (or range), sourced from public vulnerability data.
- **Benchmark Case**: A held-out binary with known ground truth used to measure precision/recall and version-resolution accuracy against the kill criteria.

## Success Criteria *(mandatory)*

### Measurable Outcomes

**Identification accuracy (Checkpoint A — heuristic signals only, no learned model)**

- **SC-001**: On the held-out benchmark set, using string/constant/control-flow signals only, top-1 library-identification precision is ≥ 80% and recall is ≥ 60% for the initial ~50-library corpus at the -O2 optimisation level.

**Identification & version accuracy (Checkpoint B — with learned similarity signal)**

- **SC-002**: On the held-out benchmark set, library-identification precision is ≥ 90% and recall is ≥ 75% across all four optimisation levels (-O0, -O2, -O3, -Os) and both compilers (gcc, clang).
- **SC-003**: For matched libraries, the version is resolved to the correct minor version in ≥ 70% of cases.
- **SC-004**: If the learned similarity signal adds fewer than 5 percentage points of recall over the heuristic signals, the tool ships heuristics-first and the learned model is parked (measured, decision recorded).

**Performance**

- **SC-005**: A 40 MB stripped binary is scanned end-to-end in under 5 minutes on a **reference
  developer laptop** — defined as an 8-core x86-64 CPU, 16 GB RAM, SSD, running a single scan process.
  The benchmark records the host it actually ran on, so the figure is reproducible rather than measured
  against an unspecified machine (Principle IV/XII).
- **SC-006**: The public web demo returns first results in under 20 seconds for the bundled sample binaries.

**Honesty (differentiator)**

- **SC-007**: 100% of reported components and version claims carry supporting evidence and a confidence score; zero components are reported without evidence.
- **SC-008**: Every scan output includes an unidentified-regions section; code the tool cannot attribute is reported there rather than misattributed (measured on the benchmark set: no unidentified code is silently dropped or forced to a nearest match).

**Reproducibility & publishability**

- **SC-009**: A from-scratch rebuild of the corpus produces an equivalent signature database (reproducible build verified).
- **SC-010**: Benchmark precision/recall and version-resolution results are published for each checkpoint, whether or not thresholds are met.

## Assumptions

- **Function boundary recovery**: First-party heuristics (prologue detection, call-target and control-flow analysis) provide the fast path; wrapping an existing headless disassembler is treated as an optional depth plug-in rather than a hard dependency. (Open question 1 — reasonable default adopted; revisit in planning.)
- **Learned similarity signal**: A similarity/embedding model is a Checkpoint-B enhancement, trained separately and shipped as portable inference inside the tool; heuristics-first is the fallback if it under-delivers (SC-004). The exact model approach and minimum viable corpus size are planning-phase decisions. (Open question 2.)
- **Corpus version scope for v1**: Versions selected are the latest per minor line plus versions associated with known published vulnerabilities, rather than every historical release, to keep build volume tractable while covering compliance-relevant versions. (Open question 3 — reasonable default adopted; adjustable.)
- **Confidence scoring method**: Library-level confidence is derived from function-level matches weighted by function distinctiveness and coverage of the library's function set; the benchmark harness is built to compare scoring approaches, so the exact combiner is a tunable, measured choice. (Open question 4.)
- **Web demo binary retention**: Uploaded binaries are processed in memory and deleted after the result is returned; they are not retained. (Open question 5b — default adopted.)
- **Corpus publication**: The signature corpus is published alongside the tool as a first-class artefact under a permissive/data licence compatible with the Apache-2.0 code licence (FR-028). (Open question 5a/5c — resolved.)
- **Demo hosting, upload cap, and abuse prevention**: The demo runs on Fortitude Omnis R&D hosting with a fixed upload size cap and request throttling for abuse prevention, no login required; specific host, cap value, and throttle limits are planning/ops decisions. (Open question 6.)
- **Scope boundaries** (carried from the brainstorm as firm non-goals): no decompilation or source reconstruction; no unpacking of packed/obfuscated binaries (detect-and-flag only); no managed/bytecode targets (.NET/JAR); no Go/Rust module-info extraction (already handled elsewhere); no licence-compliance reporting beyond passing through a library's known licence; no hosted SaaS with accounts/billing/tenancy; no deep vulnerability triage/reachability/exploitability (that is OSPulse's role); no kernel modules or bootloaders.
- **Downstream integration**: SBOMs produced are consumable by OSPulse for drift/abandonment tracking; the interface for that is a downstream concern, not part of this spec's deliverables.
- **Format sequencing**: ELF (x86-64, then AArch64) is delivered first; PE and Mach-O follow once ELF is working. Checkpoint A (heuristics) precedes Checkpoint B (learned signal); the web demo is designed from the start but delivered after the CLI, corpus, and benchmark.
- **Primary compilers/optimisation matrix**: gcc and clang at -O0, -O2, -O3, -Os are the reference build matrix, matching the benchmark's stated axes.
