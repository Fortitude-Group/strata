# Contract: GitHub Action wrapper & container image

**Stability**: Public CI surface — SemVer'd input/output. (FR-021)

## GitHub Action (`fortitude-group/strata-action` or bundled `action.yml`)
### Inputs
| Input | Required | Default | Notes |
|-------|----------|---------|-------|
| `binary` | yes | — | path to the binary to scan |
| `format` | no | `cyclonedx` | `cyclonedx` \| `spdx` \| `both` |
| `corpus` | no | pinned | corpus version/path |
| `fail-on` | no | `error` | `error` \| `findings` \| `never` — maps exit code (2) to job failure |
| `output` | no | `strata.sbom` | artefact path |

### Outputs
| Output | Notes |
|--------|-------|
| `sbom-path` | path to the generated SBOM (uploaded as a workflow artefact) |
| `component-count` | identified components |
| `cve-count` | known CVEs across identified components |
| `exit-code` | underlying `strata` exit code (see `contracts/cli.md`) |

### Behaviour
- Runs the **container image** (below) — no runtime install in the runner (FR-021).
- Non-interactive; `fail-on` maps CLI exit code 2 (findings needing attention) to pass/fail per policy.
- Re-runs cleanly on each new binary (US4 — vendor sends a new blob ⇒ SBOM regenerates).

## Container image
- **Base**: minimal Linux (distroless/alpine-class) containing only the self-contained `strata`
  executable + bundled corpus + native deps (Capstone, ONNX Runtime) — **no .NET/Python install**
  required by the user (FR-021).
- **Entrypoint**: `strata`; passes through the CLI contract verbatim.
- **Tags**: image tag pins tool + corpus version (Principle II).

## Invariants (tests)
1. `docker run … strata scan sample.bin` on a runtime-free host produces a valid SBOM (FR-021).
2. Action with `fail-on: findings` fails the job when identified components carry CVEs (exit 2).
3. Same binary via Action and via local CLI ⇒ identical components (shared engine + pinned corpus).
