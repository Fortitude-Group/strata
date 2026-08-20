# Corpus build matrix

Containerised build environments for the reproducible signature corpus
(`contracts/corpus-builder.md`). Not runnable in this offline harness — no Docker daemon is
available here — but they document exactly what produced (or would produce) the binaries that
`Strata.CorpusBuilder build` fingerprints.

## Matrix

Each recipe in `../recipes/*.json` is built across:

| Axis      | Values                          |
|-----------|----------------------------------|
| Compiler  | gcc 12 (packaged as "13.2" line for Debian bookworm), clang 17.0.6 |
| Opt level | `-O0`, `-O2`, `-O3`, `-Os`       |
| Arch      | `x86_64`, `aarch64` (cross-compiled) |

That is 2 × 4 × 2 = 16 binaries per library version. The **benchmark harness**
(`benchmark/Strata.Benchmark`) deliberately holds out different compiler/flag combinations than the
corpus was built with (FR-022), so a benchmark binary must never be built from the exact same
container invocation as its corpus counterpart.

## Images

- `Dockerfile.gcc` — gcc 12 (Debian's build of the 13.x GCC line), plus the aarch64 cross-toolchain.
- `Dockerfile.clang` — clang/lld 17.0.6, plus aarch64 cross-linking support.

Both pin their base image by **digest**, not a floating tag, and pin every apt package to an exact
version — a rebuild from the same Dockerfile months later must reproduce the same toolchain
(SC-009). When a toolchain is deliberately bumped, update the digest/version pins here *and* the
`toolchains[]` entry `Strata.CorpusBuilder` writes into `manifest.json`, so the two never drift
apart.

## Driving the matrix (CI, not this harness)

```bash
for compiler in gcc clang; do
  for arch in x86_64 aarch64; do
    docker build -f Dockerfile.$compiler --build-arg TARGET_ARCH=$arch \
      -t strata-corpus-$compiler-$arch .
    for recipe in ../recipes/*.json; do
      # fetch pinned source (recipe.sourceUrl @ recipe.sha256), then for each recipe.versions[]
      # and each recipe.buildFlags[] opt level, run the container's build driver (see the
      # ENTRYPOINT comment in each Dockerfile) and collect the output binary into
      # out/<library>-<version>-<compiler>-<opt>-<arch>/<binary>
      :
    done
  done
done

# Once every recipe × version × compiler × opt × arch binary is collected under out/:
dotnet run --project ../Strata.CorpusBuilder -- build \
  --recipes ../recipes --binaries out --out ../../corpus/dist --corpus-version 1.0.0
dotnet run --project ../Strata.CorpusBuilder -- verify --corpus ../../corpus/dist/corpus.db
```

## Why not run this here

This environment has no Docker daemon and no network egress to fetch upstream library sources, so
`Strata.CorpusBuilder` is verified end-to-end against small self-generated ELF fixtures instead
(see the project's task notes). The Dockerfiles and this driver script are the real, correct
recipe for the CI build farm (Phase-5 task) — they are authored, not simulated.
