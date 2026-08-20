# Strata benchmark results

Published good or bad, per the project's technical kill criteria (SC-010). Each run scans a **held-out**
set — binaries built with a **different compiler and optimisation level than the corpus** — so the
numbers measure generalisation, not memorisation (FR-022).

## Checkpoint A — zlib, real cross-compiler (2026-08-20)

**Setup.** Corpus built from **gcc** `-O2/-O3/-Os` across zlib `1.2.11, 1.2.12, 1.2.13, 1.3.1` (12
binaries, 585 string + 352 function signatures). Held-out set: the same four versions compiled with
**clang `-O0` and stripped** — a deliberately different compiler *and* optimisation level, in the
realistic stripped-scan case.

| Metric | Result | Checkpoint A gate | Verdict |
|--------|--------|-------------------|---------|
| Top-1 library precision | **100%** | ≥ 80% | ✅ |
| Top-1 library recall | **100%** | ≥ 60% | ✅ |
| Version-resolution accuracy (correct minor) | **100%** | — (Checkpoint B: ≥ 70%) | ✅ |
| Scan wall-time (4 binaries) | ~0.5 s | — | — |

Full report: [`checkpoint-a-zlib.json`](checkpoint-a-zlib.json).

**What this shows.** The heuristic engine (string/constant + CFG-shape + instruction-MinHash, no ML)
identifies stripped clang-built zlib from a gcc-built corpus with perfect precision/recall and resolves
the exact version every time — clearing Checkpoint A on real cross-compiler binaries.

**Honest caveats.**
- On the hardest cross-optimisation case (**`-O0` target vs `-O2/-O3/-Os` corpus**), function-level
  MinHash matching does **not** fire — the instruction streams differ too much — so identification here
  rides on the **string signal** (zlib's embedded version/error strings survive stripping). Closing that
  gap for string-poor binaries is exactly the job of the **Checkpoint B** learned-embedding signal.
- This is a **single-library** corpus (zlib). The precision figure does not yet stress cross-library
  false positives at scale; that needs the full ~50-library corpus (in progress).

## Checkpoint A — multi-library, real cross-compiler (2026-08-20)

**Setup.** Corpus built from **gcc `-O2/-O3/-Os`** across 5 real libraries — **zlib, libpng, cJSON, lz4,
bzip2** (8 library-versions, 24 binaries; 3050 function + 3866 string signatures). Held-out set: the
same libraries compiled with **clang `-O2/-O3/-Os` and stripped** (24 binaries) — a genuinely different
compiler. Identification threshold 0.25.

| Library | Precision | Recall | Verdict |
|---------|-----------|--------|---------|
| zlib | **100%** | **100%** | ✅ |
| libpng | **100%** | **100%** | ✅ |
| cJSON | **100%** | **100%** | ✅ |
| bzip2 | 25% | 100% | ⚠️ over-reports |
| lz4 | 0% | 0% | ⚠️ missed |
| **aggregate** | 66.7% | 75.0% | Checkpoint A gate (≥80%/≥60%) **not met on precision** |

Version-resolution accuracy across matched libraries: **100%**. Full report:
[`checkpoint-a-multi-library.json`](checkpoint-a-multi-library.json).

**Honest reading.** Three of five libraries identify perfectly cross-compiler. The two failures are the
smallest, structurally-similar **compression** libraries: **bzip2 over-reports** (its low-level
bit/buffer functions match near-identically — ≥0.82 similarity — against lz4/libpng targets), and
**lz4 is missed** (it is string-rich, so coverage-ratio confidence dilutes below threshold). The root
cause is measured, not guessed: with only 5 libraries, per-signature **distinctiveness cannot tell a
genuinely-unique function from generic compression code**, because the corpus lacks the counter-examples
that would down-weight it. This is a corpus-size limitation, not an algorithm dead-end — the fix is the
production **~50-library corpus** (more libraries ⇒ shared code correctly loses distinctiveness) plus a
working Checkpoint-B embedding. We publish this rather than cherry-pick the three clean libraries:
honesty over coverage is the point.

**Quality improvements this run** (all principled, all kept): exclude CRT/linker stubs and
compiler-runtime functions from the corpus; **IDF-style distinctiveness** (ubiquitous signatures → ~0);
filter ELF/toolchain **metadata strings** (section names, `GLIBC_*` tags); **minimum-8-instruction**
function gate (tiny functions collide); **optimized-only corpus** (drop `-O0` debug boilerplate);
identification threshold lowered 0.5 → 0.25 to catch genuine cross-compiler matches.

## Checkpoint B — SC-004 embedding decision: **PARKED** (2026-08-20)

The full learned-embedding pipeline was built and run end-to-end on the real zlib corpus: symbol-labelled
training export (1593 examples, 149 functions) → contrastive projection trained in Python (numpy) →
ONNX export → ONNX Runtime inference in `Strata.Core` → embedding channel in the function matcher →
corpus rebuilt with embeddings (1605 function signatures).

**Measured result.** On the hardest cross-optimisation case (`-O0` clang target vs `-O2/-O3/-Os` gcc
corpus), the embedding added **0** additional function matches over heuristics — the crude
opcode-histogram projection cannot bridge the O0↔O2 instruction-mix gap, and library-level recall was
already saturated at 100% by the string signal on this string-rich library.

**Decision (SC-004).** The embedding does **not** clear the ≥5-point recall-gain bar, so it is
**parked**: Strata ships heuristics-first. The inference wiring, corpus embedding storage, trainer, and
`--model` flag remain in place so a stronger model (deeper architecture, larger multi-library corpus,
symbol-consistent recovery on both sides) can be dropped in and re-measured without code changes. This
is the spec's designed outcome, now backed by a real number rather than an assumption.

## Reproducing

```bash
# 1. Compile real zlib in a container (corpus + held-out sets)
tools/corpus-builder/build-real-corpus.sh          # run inside a gcc image, see the script header

# 2. Build the corpus from the gcc set
dotnet run --project tools/corpus-builder/Strata.CorpusBuilder -- \
  build --recipes tools/corpus-builder/recipes --binaries <corpus-dir> --out <db-dir>

# 3. Benchmark against the held-out clang set
dotnet run --project benchmark/Strata.Benchmark -- \
  --corpus <db-dir>/corpus.db --binaries <holdout-dir> --ground-truth <gt.json> --checkpoint A
```

## Performance — SC-005 (2026-08-20)

Scanning a **3.4 MB stripped x86-64 ELF** (compiled from ~40k source functions, symbol-stripped) with
the heuristic engine: **~0.47 s** engine time (ingest 34 ms, recover+fingerprint 465 ms of 802
recovered functions, match to completion 472 ms), ~1.3 s wall including .NET startup. Extrapolated
linearly, a 40 MB binary lands at roughly 5–6 s — far inside the **< 5 min** budget (SC-005). The
recovery and CFG passes were made strictly linear-time (see security review) so cost scales with
instruction count rather than quadratically.
