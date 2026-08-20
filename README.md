# Strata: Binary-to-SBOM

**Strata reads the layers in software you didn't build.** Give it a compiled, stripped, possibly
statically-linked native binary, with no source and no debug info, and it produces a **CycloneDX / SPDX SBOM
of the open-source libraries and versions compiled into it**, each with a **confidence score** and the
**evidence** behind it.

> Strata produces *evidence toward* an SBOM. It does **not** certify or confer CRA compliance:
> compliance is the user's responsibility. Every reported component carries its evidence and a
> confidence; code it cannot place is reported as *unidentified*, never guessed.

Built by **Fortitude Omnis** as an open (Apache-2.0), CI-friendly, honest alternative to closed binary
composition analysis. See [the specification](specs/001-strata-binary-sbom/spec.md).

## Why

The EU Cyber Resilience Act obliges manufacturers to maintain an SBOM for products with digital
elements. A large share of shipped software arrives as **binaries**: firmware, OEM drivers, third-party
SDKs, vendor DLLs, legacy components whose source is gone. Manifest- and symbol-based SBOM generators
(syft, trivy, cdxgen) return almost nothing against a stripped static ELF. Strata targets exactly that
gap for native C/C++ code, with **per-match evidence** as the differentiator.

## How it works

```
ingest ─▶ recover functions ─▶ fingerprint ─▶ match corpus ─▶ SBOM + evidence
(ELF/PE/    (Iced x86-64        (strings/consts,   (string signal +      (CycloneDX 1.6,
 Mach-O)     linear sweep,       CFG-shape hash,    MinHash-LSH function   SPDX 2.3, reports,
             CFG build)          instruction        matching, fused        honest unidentified
                                 MinHash)            by noisy-OR)           regions)
```

- **Multi-signal, cheapest-first.** String/constant references, a register-allocation-robust CFG-shape
  hash, and a normalised-instruction MinHash. Signals fail independently, so their agreement is meaningful.
- **Honest versions.** Versions resolve to an exact value where the evidence is unambiguous, otherwise a
  bounded range (the intersection of the present functions' version ranges), never more precise than the
  evidence supports.
- **Deterministic.** The same binary + corpus yields byte-identical output (`--deterministic`).
- **Thin CVE cross-reference.** Identified `library@version` pairs are mapped to known CVEs from a pinned
  OSV snapshot (range-aware). Deep triage is out of scope.

## Quick start

```bash
dotnet build Strata.slnx -c Release
dotnet run --project src/Strata.Cli -- scan firmware.bin --format both -o firmware.sbom --report text
```

Exit codes (pipeline-gating): `0` success · `1` error · `2` findings need attention (CVEs / packing) · `3` usage.

```bash
strata scan <binary> [--format cyclonedx|spdx|both] [--output <path>]
                     [--report text|json|none] [--corpus <path>]
                     [--min-confidence <0..1>] [--deterministic] [--vuln on|off]
strata corpus [verify] [--corpus <path>]
strata version
```

Ships as a **self-contained single-file executable** (no runtime install) and a **runtime-free container
image**; a **GitHub Action** (`action.yml`) wraps it for CI.

## Repository layout

| Path | What |
|------|------|
| `src/Strata.Core` | The engine: ingestion, recovery, disassembly, fingerprinting, matching, versioning |
| `src/Strata.Corpus` | Signature database (SQLite) + seed corpus |
| `src/Strata.Sbom` | CycloneDX + SPDX emitters, reports |
| `src/Strata.Vuln` | Thin OSV/NVD cross-reference |
| `src/Strata.Cli` | The `strata` CLI |
| `src/Strata.Web` | Public Blazor demo (progressive reveal, capped/in-memory uploads) |
| `tools/corpus-builder` | Reproducible corpus build farm (containerised) |
| `tools/ml-training` | Off-ship-path embedding training (numpy, ONNX export) |
| `benchmark/Strata.Benchmark` | Held-out precision/recall harness with kill-criteria gates |

## Status

R&D build. The x86-64 (Iced) and AArch64 (Capstone) engines, all three container formats (ELF/PE/Mach-O
ingestion), the string + function signals, the learned-embedding signal, SBOM output, CVE
cross-reference, the web demo, the corpus builder, and the benchmark harness are implemented and tested.

On a 54-library real cross-compiler benchmark (corpus built with gcc, targets stripped and built with
clang), Strata clears both kill-criteria checkpoints: Checkpoint A at 100% precision on the heuristic
signals, and Checkpoint B with the embedding lifting recall from 77.6% to 90.2% (past the 5-point bar).
48 of 54 libraries identify at 100% precision and 100% recall, including mbedTLS and expat. The
remaining work is growing the reference corpus toward the common few-hundred libraries; the mechanism
and the harness are in place. See [`docs/benchmarks/`](docs/benchmarks/README.md) and
[`specs/001-strata-binary-sbom/tasks.md`](specs/001-strata-binary-sbom/tasks.md).

Benchmarks (precision, recall, version accuracy) are published good or bad, per the project's technical
kill criteria.

## License

Apache-2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE). The signature corpus is published under a
compatible permissive/data licence.
