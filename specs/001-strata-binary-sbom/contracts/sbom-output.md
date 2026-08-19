# Contract: SBOM output (CycloneDX + SPDX + reports)

**Stability**: Public output contract — SemVer'd. Consumers (incl. OSPulse) pin the schema. (Principle II)

## CycloneDX (primary, FR-016)
- **Spec version**: CycloneDX **1.6**, JSON.
- Each identified library → one `component` (`type: library`), with:
  - `name`, `version` (exact) OR `properties["strata:versionRange"] = "low–high"` when ranged (FR-013).
  - `purl` where derivable; `licenses` = pass-through known licence only (non-goal to analyse).
  - `evidence`: CycloneDX `evidence.occurrences` / `evidence.identity` populated from Strata
    `EvidenceRecord`s (matched functions, strings, constants). **≥1 evidence item required** (FR-014).
  - `properties`:
    - `strata:confidence` = 0.0–1.0 (R9)
    - `strata:versionBasis` = summary of version-distinguishing evidence (R8)
- **Unidentified regions** (FR-015): emitted as a top-level `properties`/`annotations` block
  `strata:unidentifiedRegions` = list of `{startAddr, endAddr, reason}`. Never omitted.
- **Vulnerabilities** (FR-019): CycloneDX `vulnerabilities[]` referencing affected components, with
  `source` (OSV/NVD) and `strata:appliesToRange` when the component version is a range.
- **Determinism** (Principle IV): `serialNumber` + `metadata.timestamp` are the only nondeterministic
  fields; `--deterministic` zeroes them so byte-identical golden output is testable.

## SPDX (FR-016)
- **Spec version**: SPDX **2.3**, tag-value and JSON.
- Each library → `Package` with `PackageName`, `PackageVersion` (or version range in a defined
  annotation), `PackageLicenseDeclared` (pass-through). Evidence + confidence carried in
  `PackageComment`/annotations. Unidentified regions in a document-level annotation.

## Reports (FR-017)
- **Text report** (`--report text`): human-readable; per component shows name, version/range,
  confidence, top evidence, CVEs; ends with an explicit "Unidentified / low-confidence regions" section.
- **JSON report** (`--report json`): the full `ScanResult` (data-model.md) serialised — components,
  evidence, version basis, unidentified regions, warnings, corpus+tool+snapshot versions.

## Invariants (asserted by contract tests)
1. Output validates against the published CycloneDX 1.6 / SPDX 2.3 schemas.
2. No `component` without ≥1 evidence item (FR-014, SC-007).
3. Every recovered function is represented either in a component's evidence or an unidentified region
   (FR-015, SC-008).
4. Version precision in output ≤ evidence precision (FR-013).
5. Output never contains the strings "CRA compliant" / "makes you compliant" (FR-018).
6. With `--deterministic`, two runs on the same (binary, corpus) are byte-identical.
