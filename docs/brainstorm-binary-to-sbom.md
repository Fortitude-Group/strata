# BRAINSTORM — Strata: Binary-to-SBOM (Fortitude Omnis R&D build)
Folder: `strata` | Status: R&D BUILD — showpiece with technical kill criteria
Use with: `/speckit.specify` — paste the sections below as the feature description.
Working name "Strata" (stratigraphy — reading the layers in a thing you didn't deposit). Rename freely.

---

## One-liner
A tool that takes a compiled, stripped, possibly statically-linked native binary with no source and no debug info, and produces a CycloneDX/SPDX SBOM of the open-source libraries and versions compiled into it, each with a confidence score and evidence — for engineers who must declare what's inside software they didn't build.

## Why (context — do not re-litigate in the spec session)
- The EU Cyber Resilience Act obliges manufacturers of products with digital elements to maintain an SBOM and handle vulnerabilities across the product's life. A large share of shipped software arrives as binaries: firmware, OEM drivers, third-party SDKs, vendor DLLs, legacy components whose source is gone. Verify current CRA dates in the spec session; the direction is not in doubt.
- Existing SBOM generators (syft, trivy, cdxgen, Microsoft sbom-tool) work from manifests, package metadata and symbol tables. Against a stripped static ELF they return next to nothing. Go and Rust binaries embed module info and are already handled; native C/C++ is the gap.
- Commercial binary-composition analysis exists (Black Duck Binary Analysis, GrammaTech CodeSentry, Finite State, Cybellum) and is expensive, closed, and not CI-shaped. Research exists (Asm2Vec, SAFE, jTrans, Trex, BinaryCorp, LibAM, B2SFinder, Karta) and is not productised. The gap is an open, honest, CI-friendly tool with per-match evidence.
- This is R&D under Fortitude Omnis: the primary purpose is a public demonstration of technical depth plus a genuinely useful tool. Secondary purpose: SBOMs it produces can be ingested by OSPulse for drift/abandonment tracking, and the CRA story reinforces OSPulse's CRA module.
- The author reads disassembly fluently (decades of assembly and engine work); the hard part is tractable for him in a way it isn't for most indie builders. That asymmetry is the point.

## Target user
A firmware engineer at a mid-sized hardware company who has been handed a 40 MB vendor-supplied ELF and a CRA compliance ticket. They know it contains zlib and some TLS library. They cannot get source. They need a defensible SBOM and a list of what to worry about, and they need it in their CI so it re-runs when the vendor sends a new blob.

## What to build (scope of THIS spec)

1. **Binary ingestion and parsing.** Accept ELF (x86-64 and AArch64 first), then PE, then Mach-O. Identify architecture, sections, entry points, imported/exported symbols if present, string tables, embedded constants. Handle stripped binaries as the default case, not the edge case.

2. **Function recovery.** Find function boundaries in stripped code (prologue heuristics, call-target analysis, control-flow recovery; optionally lean on a headless disassembler for the hard cases). Build a per-function control-flow graph and basic-block representation.

3. **Multi-signal fingerprinting.** Per function, compute several fingerprints, cheapest first:
   - string and constant references (crypto S-boxes, CRC tables, magic numbers, error strings, version strings);
   - CFG-shape hash (basic-block count, edge structure, loop nesting) robust to register allocation;
   - normalised instruction-sequence hashes (opcode mnemonics with operands abstracted);
   - a learned embedding (trained on the corpus below, exported to a portable runtime) for fuzzy matching across compilers and optimisation levels.

4. **Reference corpus.** A build farm that compiles a curated list of open-source libraries across versions × compilers (gcc, clang) × optimisation levels (-O0, -O2, -O3, -Os) × targets, extracts the same fingerprints, and stores them in an indexed signature database. Start with roughly 50 libraries (zlib, OpenSSL, mbedTLS, wolfSSL, libpng, libjpeg-turbo, curl, sqlite, expat, libxml2, pcre, bzip2, xz, lz4, zstd, json-c, cJSON, mosquitto, lwIP, FreeRTOS-adjacent bits…) and grow to a few hundred. Corpus building is automated and repeatable; the corpus is versioned and publishable.

5. **Matching and version resolution.** Match target functions against the corpus, aggregate function-level hits into library-level evidence, then resolve version by the set of functions present/absent and by version-specific strings and constants. Output a confidence score per library and a version range (exact where possible, "1.2.8–1.2.11" where not). Never report a version more precisely than the evidence supports.

6. **SBOM output.** Emit CycloneDX (JSON) and SPDX, with evidence attached per component: which functions matched, which strings, which constants, confidence. Include a plain-text and a JSON report. Include a "low confidence / unidentified code regions" section so the tool is honest about what it couldn't place.

7. **Vulnerability cross-reference (thin).** Map identified library@version pairs to known CVEs via public data (OSV/NVD). Keep this thin — the value is the identification; OSPulse does the serious downstream work.

8. **CLI and CI integration.** A single-command CLI (`strata scan firmware.bin --format cyclonedx`) with exit codes suitable for pipelines, a GitHub Action wrapper, and a container image.

9. **Public web demo.** Upload a binary (size-capped), see results stream in as libraries light up, with the evidence expandable per match. Pre-loaded sample binaries (a router firmware extract, a stripped static hello-world linked against several libs, a vendor-style DLL) so visitors see the effect without uploading anything. This is the showpiece; budget for it properly.

10. **Benchmark harness.** A held-out test set of binaries with known ground truth (built with different compiler versions/flags than the corpus) to measure precision/recall of library identification and version resolution. Results are published on the R&D page, good or bad.

## Explicitly OUT of scope (for this spec)
- Decompilation to source, or any attempt to reconstruct code.
- Obfuscated/packed binaries and anti-analysis handling. Detect and flag packing; do not unpack.
- Managed/bytecode targets (.NET assemblies, JARs): metadata-rich and mostly solved by existing tools. Possible cheap follow-on, not this spec.
- Go/Rust module-info extraction: already handled by syft et al.; don't duplicate.
- Licence compliance reporting beyond passing through the library's known licence.
- A hosted SaaS with accounts, billing, or tenancy. The web demo is a demo.
- Deep vulnerability triage, reachability, or exploitability. That's OSPulse / project 5.
- Kernel modules and bootloaders.

## Technical kill criteria (write these into the benchmark harness from day one)
- Checkpoint A (corpus and heuristics only, no ML): on the held-out set, top-1 library identification precision ≥ 80% and recall ≥ 60% for the initial ~50-library corpus at -O2. If string/constant/CFG heuristics can't reach this, the corpus or function recovery is broken; fix before adding ML.
- Checkpoint B (with embeddings): precision ≥ 90%, recall ≥ 75% across all four optimisation levels and both compilers; version resolved to the correct minor version in ≥ 70% of matched libraries. If embeddings add < 5 points of recall over heuristics, ship heuristics-first and park the model.
- Performance: a 40 MB stripped ELF scans in under 5 minutes on a developer laptop; the web demo returns first results in under 20 seconds for the bundled samples.
- Instant reconsideration: a credible open-source tool ships the same capability with published benchmarks better than Checkpoint B targets. Then pivot Strata to the evidence/CI/OSPulse-integration layer on top of it rather than competing on matching.

## Constraints & guardrails
- Honesty over coverage: every reported component carries evidence and a confidence; unidentified regions are reported as such. No silent guesses. This is the differentiator against commercial tools and the thing that makes the write-up credible.
- Never describe output as "CRA compliant" or the tool as "making you compliant". It produces evidence for an SBOM; compliance is the user's responsibility. Cite primary sources on the site.
- Stack preference as a constraint, not a design: the author's default is C#; a single-binary CLI that runs without a Python install is strongly preferred for the user-facing tool. ML training can live in Python; inference should ship via a portable runtime inside the CLI. Disassembly may use an existing engine via bindings; function recovery and fingerprinting logic are first-party.
- Corpus builds run in containers and are reproducible; the corpus and the benchmark set are publishable artefacts in their own right (and a decent secondary blog post).
- Public repo from day one if the repo is going to be public at all — no "clean it up later". Licence to be decided in the spec session (see open questions).
- Sequencing: ingestion + function recovery + heuristic fingerprints + corpus builder + CLI + benchmark harness first (Checkpoint A). Embeddings second (Checkpoint B). Web demo third, but designed from the start. PE and Mach-O after ELF works.
- Keep fleet/vehicle telematics out of the sample binaries and the marketing copy.

## Open questions for the spec session
1. Function boundary recovery: first-party heuristics only, or wrap a headless disassembler (Ghidra/radare2) for the hard cases and accept the dependency? Likely answer: first-party for the fast path, optional plug-in for depth.
2. Embedding model: train a Siamese/contrastive model on the corpus (function pairs across compiler/opt variants), or adopt a published pre-trained binary-similarity model and fine-tune? What's the minimum corpus size before training is worthwhile?
3. Corpus scope for v1: ~50 libraries × 2 compilers × 4 opt levels × N versions is a lot of builds. Which versions matter (every release, or only those with CVEs plus the latest per minor)?
4. Confidence scoring: how is library-level confidence derived from function-level matches — weighted by function uniqueness, by coverage of the library's function set, or a learned combiner? The benchmark harness should be able to compare approaches.
5. Licence and publication: OSS licence choice (permissive vs copyleft), whether the signature corpus is published alongside the code, and whether the web demo retains uploaded binaries (default: no, process in memory, delete).
6. Demo hosting: where does the web demo run, with what upload size cap, and how is abuse prevented without a login?
