# Contract: CLI command surface (`strata`)

**Stability**: Public contract — SemVer'd (Principle II). Breaking change to a command, flag, or exit
code ⇒ MAJOR bump + migration note.

## Commands

### `strata scan <binary> [options]`
Scan a native binary and emit an SBOM. (FR-001, FR-016, FR-020)

| Option | Values | Default | Notes |
|--------|--------|---------|-------|
| `--format` | `cyclonedx`, `spdx`, `both` | `cyclonedx` | SBOM output format (FR-016) |
| `--output`, `-o` | path | stdout | write SBOM document(s) here |
| `--report` | `text`, `json`, `none` | `text` | human/machine report incl. unidentified regions (FR-017) |
| `--corpus` | path or version | bundled/pinned | signature DB to match against (Principle II) |
| `--vuln` | `on`, `off` | `on` | thin CVE cross-reference (FR-019) |
| `--min-confidence` | 0.0–1.0 | `0.5` | components below are listed under low-confidence, not omitted |
| `--deterministic` | flag | off | zero out serialNumber/timestamp for golden tests (Principle IV) |
| `--plugin` | `none`, `ghidra`, `radare2` | `none` | optional deep function recovery (R2) |

### `strata corpus <verb>`
`info` (show pinned corpus manifest), `verify` (check DB schema + hashes), `pin <version>`.

### `strata benchmark [options]`
Run the held-out benchmark and print/emit precision, recall, version accuracy, and per-checkpoint
pass/fail (FR-022/023, SC-001/002/003). `--checkpoint A|B`, `--report <path>`.

### `strata version`
Print tool version, corpus version, embedding-model version (or "parked").

## Exit codes (FR-020 — pipeline-gating)
| Code | Meaning |
|------|---------|
| `0` | Success — scan completed, no attention flag |
| `1` | Error — bad input, unreadable binary, internal failure (FR-004) |
| `2` | Completed with findings needing attention — e.g. identified components carry known CVEs, or packing detected (FR-005/019). Distinct from success so CI can gate. |
| `3` | Usage error — bad arguments |

## Global behaviour
- Fully **non-interactive** (no prompts) — CI-safe (FR-020).
- No separate language runtime required to run any command (FR-021).
- Structured logs to stderr (`--log-level`); SBOM/report to stdout or `--output` (Principle IV).

## Example
```
strata scan firmware.bin --format both -o firmware.sbom --report json --deterministic
# exit 2 → SBOM written (CycloneDX+SPDX), machine report shows components+evidence+CVEs+unidentified regions
```
