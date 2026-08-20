# Strata ML training (off ship path)

Trains the Checkpoint-B function-similarity model on corpus function-variant pairs and exports it to
**ONNX** (research.md R7). This is the only Python in the project and never runs inside the shipped CLI.
Inference ships via ONNX Runtime bundled in the self-contained build.

`train_similarity.py` reads the labelled feature export from `Strata.CorpusBuilder train-export`
(each example is an opcode-histogram feature plus a symbol label plus a build variant), forms positive
pairs (same symbol, different compiler/opt) and negatives, trains a linear projection with a contrastive
objective (numpy, no torch), and exports a MatMul ONNX graph. Cosine similarity is applied in
`Strata.Core`, so the model itself is just the projection.

## Status: ACHIEVED and shipping (2026-08-20)

On the 21-library real cross-compiler benchmark, the model retrained on 15,680 labelled examples
(4,527 functions, 48-dim) lifted aggregate recall from **76.0% to 92.0%** (a 16-point gain, well past
the SC-004 5-point bar), at a precision cost of 100% to 97.2%. It recovered three of the four
string-poor libraries the heuristics miss (libdeflate, monocypher, zstd). The model trains on gcc
builds and generalises to the held-out clang builds.

The corpus builder `--model` flag bundles the trained model into the corpus artefact as `model.onnx`,
and `strata scan --corpus <db>` auto-loads it. See `docs/benchmarks/` for the measured numbers.

## Reproduce

```bash
# 1. Export labelled features from a built corpus set (gcc builds, symbols present)
dotnet run --project tools/corpus-builder/Strata.CorpusBuilder -- \
  train-export --binaries <corpus-dir> --out features.json

# 2. Train and export ONNX
python tools/ml-training/train_similarity.py --features features.json --out model.onnx --dim 48 --epochs 150

# 3. Rebuild the corpus with embeddings (bundles model.onnx into the corpus dir)
dotnet run --project tools/corpus-builder/Strata.CorpusBuilder -- \
  build --recipes tools/corpus-builder/recipes --binaries <corpus-dir> --out <db-dir> --model model.onnx
```
