# Contract: Corpus builder & benchmark harness

**Stability**: Internal tooling that produces two public artefacts — the signature DB
(`contracts/signature-db.md`) and the published benchmark report. Reproducibility is the contract.

## Corpus builder (`tools/corpus-builder`) — FR-009/010/011
- **Input**: `recipes/*.yaml` — per library: `name`, `purl`, `known_license`, `source_url` + hash,
  `versions` (resolved by policy), `build_flags`.
- **Version-selection policy**: **latest per minor line + versions flagged with known CVEs** (R11).
- **Build matrix**: {gcc, clang (pinned images)} × {-O0,-O2,-O3,-Os} × {x86_64, aarch64}.
- **Process**: containerised build → run `Strata.Core` fingerprinting (same code as scan, Principle I) →
  write `corpus.db` + `corpus.lsh` (+ `corpus.hnsw` if model) + signed `manifest.json`.
- **Reproducibility invariant** (SC-009): re-running with the same manifest toolchains/flags yields an
  equivalent DB (`buildReproducibleHash` stable); no timestamps/host paths in signatures.
- **Environment**: ephemeral CI containers — Principle X **exempt**, fully automatable (auto-approve OK).

## Benchmark harness (`benchmark/Strata.Benchmark`) — FR-022/023
- **Input**: held-out ground-truth set — binaries built with compiler versions/flags **deliberately
  different** from the corpus (FR-022); each has `TrueComponents = (library, version)[]`.
- **Outputs**: per-library and aggregate **top-1 precision, recall, version-resolution accuracy**, plus
  scan wall-time; per-checkpoint **pass/fail**:
  - **Checkpoint A** (heuristics only, ~50-lib, -O2): precision ≥ 80%, recall ≥ 60% (SC-001).
  - **Checkpoint B** (with embeddings, all 4 opt levels + both compilers): precision ≥ 90%, recall ≥ 75%;
    correct minor version ≥ 70% of matched libs (SC-002/003); embedding-vs-heuristics recall delta
    reported to drive the SC-004 ship/park decision.
  - **Performance**: 40 MB stripped binary < 5 min (SC-005).
- **Publication invariant** (SC-010): the report is published **regardless of pass/fail** — no hiding a
  failed checkpoint.
