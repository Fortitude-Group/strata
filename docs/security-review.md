# Security review — Strata as an untrusted-binary and public-upload surface

**Scope.** Strata's entire purpose is parsing binaries it did not produce — vendor DLLs, firmware
extracts, third-party SDKs — and, via the web demo, accepting files from anonymous members of the
public. This review treats every binary as **adversarial input** and the web demo as an **anonymous,
unauthenticated attack surface**, and asks: what happens when the input is crafted to hurt the tool,
not just fail to match a library?

**Method.** Every claim below was checked against the actual source in `src/Strata.Core`,
`src/Strata.Cli`, `src/Strata.Web`, and the relevant `.csproj`/`Directory.Build.props` files — not
inferred from documentation or commit messages. File:line references are given so each finding can be
re-verified directly. Severities: **info** (no action needed, noted for awareness) · **low** (worth
fixing, low likelihood or low impact) · **medium** (real risk, should be fixed before wider exposure) ·
**high** (concretely exploitable, fix before this surface is trusted with untrusted/anonymous input).

---

## (a) Binary-parsing safety — bounds checking in the readers

**Reviewed:** `Ingestion/ElfReader.cs`, `Ingestion/PeReader.cs`, `Ingestion/MachOReader.cs`,
`Ingestion/StringConstantExtractor.cs`, `Ingestion/FormatDetector.cs`.

All three container readers are wrapped in a `try { ... } catch (System.Exception)` around their entire
body (`ElfReader.ReadSections`/`ReadFunctionSymbols`, `PeReader.Read`, `MachOReader.Read`), and fall
back to an empty/`Unknown` result on failure rather than propagating. Individual field reads do use
explicit length checks before the risky ones (e.g. `ElfReader.ReadSections` checks
`shoff + (shnum*shentsize) > data.Length` before walking the section table; `PeReader.Read` checks
`baseOff + 40 > data.Length` per section). `StringConstantExtractor.Extract` is a simple forward index
scan with no slicing at all, so it has no bounds-check surface. `FormatDetector` checks `data.Length`
before every multi-byte read.

**What is *not* independently bounds-checked:** a few offsets are computed as `ulong` from
attacker-controlled fields and then narrowed with an explicit `(int)` cast before being used in a
`Span` slice — e.g. `ElfReader.ReadSections`, the section-header-string-table lookup:

```csharp
ulong shstrOffEntry = shoff + ((ulong)shstrndx * shentsize);      // shstrndx is NOT validated < shnum
ulong shstrOff = ReadU64(data.Slice((int)shstrOffEntry + 24, 8), le);
```

and in `ElfReader.ReadFunctionSymbols`:

```csharp
int baseOff = (int)(symtab.Offset + (i * entSize));
if (baseOff + entSize > data.Length) { break; }
```

`shstrndx` (the section-header string-table index) is read straight from the file and never checked
against `shnum`, and a `ulong → int` cast on an attacker-supplied 64-bit offset can wrap. In every case
this review found, the *consequence* of a wrapped/out-of-range offset is a `Span` index that throws
`ArgumentOutOfRangeException` — which the enclosing `try/catch` in the same method catches, so **no
crash and no memory-unsafety results**. The residual risk is narrower: a wrap that lands *in-bounds* on
an unrelated file offset would silently read garbage bytes as a section/symbol name — a data-quality
bug (a mislabelled or spuriously-matched section), not a safety bug, and it only affects the ELF path.

**Verdict: low.** No unbounded/unsafe read was found — every reader is either explicitly
length-checked or protected by an enclosing exception handler that itself has no failure mode. The
remaining exposure is *defense-in-depth*: bounds safety currently depends on the CLR's own array-bounds
exception rather than an explicit pre-check for every offset.

**Recommendation:** validate `shstrndx < shnum` explicitly, and guard the `ulong → int` casts with an
explicit `<= data.Length` range check before slicing, rather than relying on the exception path. This
is a hardening change, not a fix for an observed unsafe read — appropriate for a follow-up, not urgent.

---

## (b) Resource limits — the size envelope, and what a crafted binary can do once inside it

**Reviewed:** `LoadOptions.MaxInputBytes` (`Abstractions.cs`), `BinaryLoader.ReadAll`
(`Ingestion/BinaryLoader.cs`), `Recovery/FunctionRecovery.cs`, `Recovery/CfgBuilder.cs`,
`Strata.Cli/ScanCommand.cs`.

**Finding 1 — no default byte cap on the CLI path (medium).** `LoadOptions.MaxInputBytes` defaults to
`0`, which `BinaryLoader.ReadAll` treats as *no limit*:

```csharp
private static byte[] ReadAll(Stream stream, long maxBytes)
{
    if (maxBytes > 0 && stream.CanSeek && stream.Length > maxBytes) { throw new OutOfEnvelopeException(...); }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    ...
}
```

`Strata.Cli.ScanCommand.Run` constructs its `ScanOptions` without ever setting `Load.MaxInputBytes`
(`ScanCommand.cs:33-37`), so a `strata scan` against an arbitrarily large file reads the whole thing
into memory (twice, transiently — once into the `MemoryStream`, once into the `byte[]` returned by
`ToArray()`) with no ceiling. This matters specifically *because* Strata's stated purpose is scanning
binaries the user did not build — CI pipelines invoking `strata scan` on a fetched/vendor artifact, or
the GitHub Action, have no protection against an oversized or maliciously bloated input exhausting the
runner. The mechanism to prevent this (`OutOfEnvelopeException`) already exists in the engine; it is
simply never wired to a default value on the one caller most exposed to untrusted input.

**Finding 2 — quadratic-time function recovery / CFG construction, unbounded by function or
instruction count (high).** This is the most concrete gap found in this review. Two independent
hot paths are each `O(n²)` in the number of decoded instructions within a section/function, with no
cap anywhere in `RecoveryOptions`:

1. `FunctionRecovery.Recover` (`Recovery/FunctionRecovery.cs:40-49`) computes one candidate function
   per distinct entry point (symbol address ∪ call-instruction target) in a section, then for **each**
   entry does a full linear scan of **all** decoded instructions in that section to slice out the
   function's body:

   ```csharp
   for (int e = 0; e < entries.Count; e++)
   {
       ...
       List<DecodedInstruction> body = instrs.Where(i => i.Address >= start && i.Address < end).ToList();
   ```

   `entries` is attacker-influenced: every `call` instruction the decoder finds contributes a new
   entry point (`EntryPoints`, `Recovery/FunctionRecovery.cs:93-98`). A section engineered to be
   almost entirely short `call`-like encodings drives `entries.Count` toward `O(instrs)`, making this
   loop `O(instrs²)`.

2. `CfgBuilder.LastInstructionIn` (`Recovery/CfgBuilder.cs:96-109`) does a full linear scan of a
   function's instruction list for **each** basic block in that function to find the block's last
   instruction:

   ```csharp
   foreach (DecodedInstruction ins in instrs)
   {
       if (ins.Address >= block.StartAddress && ins.Address < block.EndAddress) { last = ins; }
   }
   ```

   For a single large, branch-dense function (e.g. the degenerate case where entry-point detection
   finds *no* call targets, so the entire section becomes one "function" with many blocks), this is
   `O(blocks × instructions)` — again effectively quadratic in section size.

**Concrete impact.** These two paths are not just theoretically quadratic — the numbers are large
enough to matter well within the *existing* 8 MiB web-demo upload cap. An ~8 MiB section built almost
entirely of short call-like x86-64 encodings decodes to on the order of 1–3 million instructions;
`entries.Count` scales with that, so the `FunctionRecovery` loop alone approaches `10¹²` element
comparisons — many minutes to hours of CPU on a single scan, on an input that is well inside every
size limit the system currently enforces (the web demo's 8 MiB cap, and even a sensibly-chosen CLI
cap). **The byte-size cap in (Finding 1) does not bound the CPU cost of this stage** — it bounds
memory for ingestion, not the algorithmic cost of recovery. This is a genuine, verified
resource-exhaustion vector against both the CLI/CI path and — despite its upload cap and per-IP
throttle — the public web demo: one accepted 8 MiB upload can occupy a demo worker for an unbounded
time, which a throttle keyed on *request count* (§(c)) does not prevent.

**Recommendation (do before this surface takes untrusted input at scale):**
- Rewrite both hot loops to be linear: `FunctionRecovery` already builds `instrs` in address order —
  binary-search (or a single co-sorted merge pass) for each entry's slice instead of `Where` over the
  whole list; `CfgBuilder.LastInstructionIn` can use the existing `byAddress` dictionary / a sorted
  scan instead of re-scanning `instrs` per block.
- Add an explicit cap to `RecoveryOptions` (e.g. `MaxFunctionsPerSection` and/or
  `MaxInstructionsPerSection`) and stop recovery early into a single `UnidentifiedRegion` with reason
  `RecoveryUncertain` past the cap — consistent with the existing "report, don't hang" philosophy
  already applied to `OutOfEnvelopeException` and packing detection.
- Wire a sane default for `LoadOptions.MaxInputBytes` in `Strata.Cli.ScanCommand` (and expose it as a
  `--max-bytes` flag), matching the pattern the web demo already uses correctly.

**Other resource paths checked and found adequate:**
- `PackingDetector.Detect` — `O(n)` entropy pass + a handful of tiny fixed signatures scanned
  naively (`O(n·5)` worst case); not attacker-amplifiable in any meaningful way.
- `StringConstantExtractor.Extract` — single forward pass, `O(n)`, no recursion, no per-match
  allocation beyond the result list.
- `FunctionEvidenceMatcher`/`LshIndex` — LSH candidate retrieval is sub-linear against the corpus by
  design; the only unbounded loop (`embeddedCorpus`, `Matching/FunctionEvidenceMatcher.cs:130-137`) is
  bounded by *corpus* size (operator-controlled), not by attacker-controlled target size.
- `EmbeddingModel.Embed` — bounded by the fixed `OpcodeHistogram.Size` (60 features); ONNX Runtime
  inference cost is constant per call.

---

## (c) The web demo — upload cap, retention, throttling, auth

**Reviewed:** `Strata.Web/Services/DemoOptions.cs`, `Strata.Web/Services/PerIpScanThrottle.cs`,
`Strata.Web/Services/ClientCircuitContext.cs`, `Strata.Web/Components/Pages/Demo.razor.cs`,
`Strata.Web/Program.cs`.

- **Upload cap: present and enforced twice.** `DemoOptions.MaxUploadBytes` defaults to 8 MiB.
  `Demo.razor.cs.OnFileSelectedAsync` rejects on `IBrowserFile.Size` (metadata only, nothing read) if
  over cap, *and* separately bounds `file.OpenReadStream(Config.MaxUploadBytes)`, so even a client that
  lies about `Size` cannot stream more than the cap. Good defense in depth. **Info.**
- **Retention: genuinely none.** Uploads and bundled samples alike are read fully into an in-memory
  `MemoryStream`/`byte[]`, scanned, and never written to disk (`ScanDemoService.OpenSampleAsync` /
  `Demo.razor.cs.OnFileSelectedAsync`); `MainLayout.razor` states this in the UI copy, and the code
  matches the claim. **Info.**
- **Per-IP throttling: implemented at two layers, but with two real gaps.** An ASP.NET Core
  `PartitionedRateLimiter` on the HTTP pipeline (120 req/min/IP, `Program.cs:21-34`) plus a
  second, purpose-built `PerIpScanThrottle` (default 10 scans/60 s/IP) guarding the expensive `Scan()`
  call itself, since a Blazor Server circuit's SignalR connection doesn't generate new HTTP requests
  for the first limiter to see. Both are keyed by IP correctly documented as the reason for the
  second layer. Two gaps found on inspection:
  - **Medium — IP is read from the raw TCP connection, with no `ForwardedHeaders` middleware
    configured.** `ClientCircuitContext` uses `accessor.HttpContext?.Connection.RemoteIpAddress`
    (`ClientCircuitContext.cs:13`) and `Program.cs` never calls `UseForwardedHeaders()`. This is
    *correctly* resistant to a client spoofing `X-Forwarded-For` to evade the throttle — but it also
    means that **if the demo is ever deployed behind a reverse proxy or CDN** (the norm for the
    Fortitude Omnis hosting model), every visitor's `RemoteIpAddress` will be the proxy's own address,
    collapsing the per-IP throttle into a single shared global bucket. That is a denial-of-service
    risk aimed at *other users*, not a security bypass: one abusive visitor exhausts the throttle for
    everyone. **Recommendation:** if/when this ships behind a proxy, configure
    `UseForwardedHeaders()` with an explicit trusted-proxy allow-list (never trust
    `X-Forwarded-For` from an untrusted edge) before relying on the per-IP throttle.
  - **Low — `PerIpScanThrottle`'s limiter dictionary never evicts.** `_limiters` is a
    `ConcurrentDictionary<string, FixedWindowRateLimiter>` that grows by one entry per distinct
    `clientKey` ever seen, for the process lifetime (`PerIpScanThrottle.cs:16,27`). Under IPv6 (where
    an attacker can cheaply present many distinct source addresses) this is a slow, unbounded memory
    leak, not an immediate DoS. **Recommendation:** evict idle entries (e.g. on a periodic sweep, or a
    bounded LRU) rather than retaining one limiter per IP forever.
  - Combined with §(b) Finding 2: the request-count throttle does **not** bound per-request CPU time,
    so a single throttled-and-accepted 8 MiB upload can still occupy a worker far longer than the
    throttle window implies.
- **No authentication: by design, and appropriately scoped.** The demo is explicitly anonymous
  (`README.md`, `contracts/web-demo.md`); the review found no incidental auth-adjacent surface (no
  session/account state, no privileged action gated only by client-side checks) that this exposes.
  **Info** — consistent with the stated design, not a gap.

---

## (d) Path handling / directory traversal in CLI outputs

**Reviewed:** `Strata.Cli/ScanCommand.cs` (`EmitSboms`), `Strata.Cli/ArgMap.cs`.

`--output <path>` is passed straight to `File.WriteAllText(path, doc)` with no normalisation,
allow-listing, or traversal check (`ScanCommand.cs:100-111`); `--corpus <path>` similarly reaches
`SqliteCorpus.Load` unsanitised. **This is not flagged as a vulnerability**: the CLI is a local tool
invoked by the same principal who supplies the path — there is no trust boundary being crossed (the
invoker already has whatever filesystem access the path would exploit). The one place this could
matter is a CI/Action context where `binary`/`output`/`corpus` values are sourced from workflow inputs
that are themselves attacker-influenced (e.g. a PR title or branch name plumbed into a workflow input)
— but that is a property of how a *caller* wires `action.yml`, not of Strata's own code, and is out of
this review's scope (Strata neither reads nor writes any path the caller didn't explicitly supply).
**Info** — no code change recommended; note for anyone wiring Strata into a CI job that accepts
untrusted workflow-dispatch inputs to validate those inputs before passing them through.

---

## (e) FR-018 compliance-claim guard

**Reviewed:** `Strata.Sbom/SbomWriter.cs`.

`SbomWriter.Emit` calls `AssertNoComplianceClaims` on every emitted document before returning it,
throwing `InvalidOperationException` if the text (case-insensitively) contains any of a fixed set of
phrasings ("cra compliant", "makes you compliant", etc. — `SbomWriter.cs:19-28`). This is a genuine,
verified hard gate — it runs unconditionally on the emit path, not just in tests, so a future change to
report/SBOM templates that accidentally introduced compliance language would fail loudly (throw) rather
than ship. `TextReport.cs` independently carries the correct disclaimer ("Strata produces evidence
toward an SBOM; it does not certify or confer CRA compliance") that this guard would also catch if it
regressed.

**Verdict: info**, with one honest caveat: this is a **fixed denylist of known phrasings**, not a
semantic check. It reliably prevents regression of *exactly this wording*, but a differently-phrased
compliance claim introduced by a future change (e.g. "meets Cyber Resilience Act requirements") would
not trip it. **Recommendation:** keep the contract tests that exercise this guard
(`contracts/sbom-output.md` invariant 5) in the review checklist for any change to report/SBOM copy,
and consider widening the denylist if new phrasings are ever discussed for the product's marketing
copy (a common way this kind of thing regresses is via marketing text getting reused verbatim in a
report template).

---

## (f) Supply chain — pinned dependencies and license posture

**Reviewed:** every `PackageReference` in `src/**/*.csproj`, `Directory.Build.props`, `LICENSE`,
`NOTICE`.

| Package | Version (pinned, exact) | Role |
|---|---|---|
| `Iced` | 1.21.0 | x86-64 decoder — pure C#, no native surface |
| `Gee.External.Capstone` | 2.3.0 | AArch64 decoder — binds the native `libcapstone` |
| `Microsoft.ML.OnnxRuntime` | 1.29.0 | optional embedding-model inference |
| `CycloneDX.Core` | 12.1.2 | CycloneDX SBOM emission |
| `Microsoft.Data.Sqlite` | 10.0.11 | corpus persistence |
| `Spectre.Console` | 0.57.2 | CLI text report rendering |

All six are pinned to an exact version (no floating `*`/range specifiers anywhere in the tree), which
is the right default for a tool whose own output must be deterministic (Principle IV) and whose supply
chain matters to its users (it produces *other people's* SBOMs). `Directory.Build.props` sets
`PackageLicenseExpression = Apache-2.0` project-wide, matching `LICENSE`/`NOTICE`, and `NOTICE`
correctly states the FR-018 non-compliance-claim posture in the artifact's own attribution file.

**One item this review could not independently verify:** the *current* license terms of each pinned
dependency were not re-fetched from each package's own repository/NuGet listing as part of this pass —
the table above reflects only what versions are pinned and that Strata's own licensing is
self-consistent, not a fresh third-party license audit. All six are commonly permissive
(MIT-family/Apache-2.0) packages with no history of licensing friction with Apache-2.0 projects, but
"commonly" is not "verified here." **Info**, with a concrete recommendation: wire a dependency
license/vulnerability check (`dotnet list package --vulnerable`, or a dedicated SCA tool such as
`dotnet-project-licenses`) into CI so this posture is continuously verified rather than asserted once.
One native-binding-specific note worth flagging separately: `Gee.External.Capstone` ships a native
`libcapstone` binary — verify at release-build time that the exact native artifact bundled matches the
pinned managed package version (no separate/looser pin on the native side), since a native binary is
not itself covered by NuGet's package-integrity story the way the managed DLL is. **Low.**

---

## Summary

| # | Area | Severity | Verified finding |
|---|---|---|---|
| 1 | (a) parsing | low | Bounds safety relies on exception-catching rather than explicit pre-checks in a few ELF offset calculations; no unsafe read found |
| 2 | (b) resource limits | **medium** | CLI never sets a default `LoadOptions.MaxInputBytes` — unbounded-size input on the path most exposed to untrusted/CI-supplied binaries |
| 3 | (b) resource limits | **high** | `FunctionRecovery.Recover` and `CfgBuilder.LastInstructionIn` are each quadratic in instruction count with no cap — a crafted binary well within the existing 8 MiB web-demo cap can drive CPU cost into the range of minutes-to-hours |
| 4 | (c) web demo | medium | Per-IP throttle keyed on raw `RemoteIpAddress` with no `ForwardedHeaders` config — collapses to one shared bucket if ever deployed behind a proxy/CDN |
| 5 | (c) web demo | low | `PerIpScanThrottle`'s limiter dictionary never evicts — slow unbounded memory growth under IP rotation |
| 6 | (d) path handling | info | No traversal risk found — CLI paths are invoker-supplied, not a crossed trust boundary |
| 7 | (e) compliance guard | info | Real, unconditional hard gate; fixed-denylist limitation noted |
| 8 | (f) supply chain | info/low | Pins are exact and consistent; license terms not independently re-verified in this pass; native Capstone binary pin worth a release-time check |

**Bottom line:** the honesty/evidence invariants (empty-evidence construction guard, FR-018 gate) are
genuinely enforced in code, not just documented — that part of the design holds up under review. The
one finding that should block treating this as safe for high-volume untrusted or anonymous input as-is
is **#3**: the quadratic recovery/CFG-construction cost is a real, verified algorithmic
denial-of-service vector that the existing size caps do not mitigate. Fixing that (linear-time
scans) plus wiring a default CLI byte cap (#2) addresses the two findings that most directly threaten
availability. #4/#5 matter specifically at the point the web demo moves behind a reverse proxy or sees
sustained abuse; neither blocks current operation.
