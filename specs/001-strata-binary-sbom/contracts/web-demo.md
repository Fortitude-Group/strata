# Contract: Public web demo

**Stability**: Public-facing surface. Behaviour (not internal transport) is the contract. (FR-024/025/026)

## Behaviour
- **Sample-first**: bundled sample binaries selectable with no upload (FR-024). Samples =
  {router-firmware extract, stripped static multi-library hello-world, vendor-style DLL}. **No
  fleet/vehicle telematics content** in samples or copy (FR-026).
- **Progressive reveal**: identified libraries appear as they are confirmed (streamed from
  `IScanner`'s `IProgress<ScanProgress>`), each with expandable per-match evidence (FR-024).
- **First result < 20 s** for bundled samples (SC-006).
- **Upload path**: user may upload a binary **within a size cap**; over-cap uploads are rejected with a
  clear message and no scan attempted (FR-025, US6 sc.3).
- **Retention = none**: uploaded bytes are processed **in memory and deleted** after the response;
  never persisted (FR-025, US6 sc.2).
- **Abuse control without login**: enforced upload size cap + per-IP/session request throttle; no
  account required (FR-025, US6 sc.4).
- **Honesty parity**: the demo shows confidence + evidence + unidentified regions exactly as the CLI
  does (FR-014/015/027); it never renders "CRA compliant" language (FR-018).

## Config parameters (set at deploy, Principle X approval)
| Parameter | Contract |
|-----------|----------|
| `MaxUploadBytes` | enforced hard cap; value set at deploy |
| `ThrottlePerIp` | requests/window; value set at deploy |
| `SampleSet` | the three bundled binaries above |
| Retention | MUST be "none / in-memory" — not configurable to "retain" |

## Invariants (tests)
1. Over-cap upload ⇒ rejected, no scan, clear message.
2. After a scan response, no uploaded-binary bytes remain in storage/temp (retention=none).
3. Streamed component order = confirmation order; evidence expandable per component.
4. Same binary via demo and CLI ⇒ same identified components (shared engine, Principle I).
