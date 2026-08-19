# Quickstart — Strata Binary-to-SBOM validation guide

Runnable scenarios that prove the feature works end-to-end. Each maps to user stories/success criteria.
Details of shapes live in `contracts/` and `data-model.md` — not duplicated here.

## Prerequisites
- .NET 10 SDK (build only; shipped CLI needs no runtime — FR-021).
- Docker (corpus builds + container image + web demo).
- A built signature corpus (`strata-corpus-<version>/`) — or run the corpus builder (Scenario 5).
- Fixture binaries under `tests/fixtures/` (small, built, known-composition — Scenario 0).

## Scenario 0 — Build & fixtures (setup)
```
dotnet build Strata.slnx -c Release
dotnet test  Strata.slnx                 # unit + contract + integration; must be green (Gate #2)
# fixtures/ contains stripped static binaries built against known libs (zlib, mbedTLS, …) with a
# recorded ground-truth manifest per fixture.
```
**Expect**: clean build (0 errors), all tests pass.

## Scenario 1 — Core scan → evidence-backed SBOM  (US1 · FR-001/016/017 · SC-007/008)
```
strata scan tests/fixtures/static-multilib.elf --format both -o out.sbom --report json --deterministic
```
**Expect**:
- Valid CycloneDX 1.6 (`out.sbom.cdx.json`) and SPDX 2.3 (`out.sbom.spdx.json`) — validate against schemas.
- Every component has ≥1 evidence item and a `strata:confidence` (FR-014, SC-007).
- Output contains a `strata:unidentifiedRegions` block (FR-015, SC-008).
- Re-running the exact command yields **byte-identical** SBOMs (Principle IV).
- Output contains no "CRA compliant" string (FR-018).

## Scenario 2 — Honest version resolution  (US2 · FR-013 · SC-003)
```
strata scan tests/fixtures/zlib-1.2.11-static.elf --report json | jq '.components[] | {name, version}'
```
**Expect**: for a fixture with version-distinguishing evidence → exact version; for one without → a range
(e.g. `1.2.8–1.2.11`) that **contains** the true version and is no narrower than the evidence (FR-013).

## Scenario 3 — Packed / unrecognised input handling  (US1 edge · FR-004/005)
```
strata scan tests/fixtures/upx-packed.elf     # → flagged packed, exit 2, no misleading component list
strata scan tests/fixtures/not-a-binary.txt   # → clear error, exit 1
```
**Expect**: packing flagged and reported non-authoritative (FR-005); non-binary rejected with actionable
message (FR-004).

## Scenario 4 — Vulnerability cross-reference (thin)  (US5 · FR-019)
```
strata scan tests/fixtures/oldssl-static.elf --report json | jq '.components[].vulnerabilities'
```
**Expect**: a component with a known-vulnerable version lists CVEs (source OSV/NVD, pinned snapshot); a
clean component lists none; range-resolved components mark `appliesToRange` (US5 sc.2).

## Scenario 5 — Reproducible corpus + benchmark (kill criteria)  (US3 · FR-009/010/022/023 · SC-001/009/010)
```
tools/corpus-builder/build.ps1 --recipes tools/corpus-builder/recipes --out strata-corpus-dev
strata corpus verify --corpus strata-corpus-dev          # manifest + schema + hashes OK
strata benchmark --checkpoint A --report bench-A.json     # held-out set, different toolchain
```
**Expect**:
- Two independent corpus builds produce equivalent DBs (`buildReproducibleHash` stable) — SC-009.
- Benchmark prints precision/recall + per-checkpoint pass/fail; Checkpoint A gate = precision ≥ 80%,
  recall ≥ 60% at -O2 (SC-001). Report is emitted whether or not it passes (SC-010).

## Scenario 6 — CI integration  (US4 · FR-020/021)
```
docker run --rm -v "$PWD:/w" ghcr.io/fortitude-group/strata scan /w/tests/fixtures/static-multilib.elf
echo "exit=$?"    # 0 success · 2 findings-need-attention · 1 error
```
**Expect**: runs on a runtime-free host (FR-021); exit code gates a pipeline (FR-020). The GitHub Action
(`contracts/github-action.md`) with `fail-on: findings` fails the job when CVEs are present.

## Scenario 7 — Web demo  (US6 · FR-024/025/026 · SC-006)
```
docker compose -f src/Strata.Web/docker-compose.yml up
# open the demo → pick a bundled sample → libraries light up with expandable evidence
```
**Expect**: first results < 20 s for bundled samples (SC-006); components stream in with per-match
evidence; an over-cap upload is rejected; an uploaded binary is not retained after the result
(FR-025); no fleet/vehicle telematics in samples (FR-026); same components as the CLI for the same binary.

## Traceability
| Scenario | User story | Key FRs | Success criteria |
|----------|-----------|---------|------------------|
| 1 | US1 | 001,016,017 | SC-007, SC-008 |
| 2 | US2 | 013 | SC-003 |
| 3 | US1 (edge) | 004,005 | — |
| 4 | US5 | 019 | — |
| 5 | US3 | 009,010,022,023 | SC-001, SC-009, SC-010 |
| 6 | US4 | 020,021 | SC-005 (via benchmark) |
| 7 | US6 | 024,025,026 | SC-006 |
