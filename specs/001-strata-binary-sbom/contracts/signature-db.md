# Contract: Signature database (corpus artefact)

**Stability**: Public, versioned artefact — `SchemaVersion` is a SemVer'd contract (Principle II).
Consumers pin a corpus version; the CLI refuses a corpus whose `SchemaVersion` it does not support.

## Artefact layout
```
strata-corpus-<version>/
├── corpus.db            # SQLite — structured signatures + metadata
├── corpus.lsh           # MinHash-LSH bands for normalised-instruction fuzzy match (R6)
├── corpus.hnsw          # HNSW index of embeddings (present iff model shipped) (R7)
└── manifest.json        # signed manifest (see below)
```

## `manifest.json` (reproducibility, SC-009)
```json
{
  "corpusVersion": "1.0.0",
  "schemaVersion": 1,
  "toolchains": [{"compiler":"gcc","version":"13.2","imageDigest":"sha256:…"},
                 {"compiler":"clang","version":"17.0","imageDigest":"sha256:…"}],
  "optLevels": ["-O0","-O2","-O3","-Os"],
  "arches": ["x86_64","aarch64"],
  "libraryCount": 50,
  "modelVersion": "sim-1.0.0 | parked",
  "buildReproducibleHash": "sha256:…"
}
```

## SQLite schema (SchemaVersion 1) — logical
- `library(id, name, purl, known_license, source_url)`
- `library_version(id, library_id, version, is_cve_flagged)`
- `corpus_function(id, library_id, name, distinctiveness)`  — `distinctiveness` = inverse cross-library
  frequency, precomputed for confidence weighting (R9)
- `signature(id, corpus_function_id, library_version_id, compiler, opt_level, arch,
   stringconst_refs, cfg_shape_hash, norm_insn_minhash, embedding NULLABLE)`
- Covering indexes on `cfg_shape_hash` and `(library_id, version)`; MinHash lives in `corpus.lsh`.

## Invariants (contract tests)
1. Signatures are produced by the **same** fingerprinting code as scan time (Principle I) — verified by
   round-tripping a known binary through both paths.
2. A reproducible rebuild (same manifest toolchains/flags) yields an equivalent `corpus.db`
   (`buildReproducibleHash` stable) — SC-009.
3. No timestamps or build-host paths leak into signatures (determinism).
4. `SchemaVersion` bump ⇒ documented migration; CLI version-gates the corpus.
