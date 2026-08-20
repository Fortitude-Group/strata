#!/usr/bin/env python3
"""Trains the Strata function-similarity embedding and exports it to ONNX (research.md R7, T069).

Input: the labelled feature export from `Strata.CorpusBuilder train-export`
(each example = an opcode-histogram feature + a symbol label + a build variant).

Model: a single learned linear projection W (feature -> embedding), trained with a contrastive
objective so functions sharing a symbol (across compilers/opt levels) embed close together and
different functions embed apart. Kept deliberately small and dependency-light (numpy + onnx, no
torch) because the ship path runs it via ONNX Runtime in-process; cosine similarity is applied in
Strata.Core, so the model itself is just the projection.

Exports a MatMul-only ONNX graph: embedding[1,D] = feature[1,F] @ W[F,D].

The decision to *ship or park* this model is not made here — it is made by the benchmark measuring
the recall delta over heuristics (SC-004).
"""
import argparse
import json
import sys

import numpy as np
import onnx
from onnx import TensorProto, helper


def load(path):
    with open(path, "r", encoding="utf-8") as fh:
        data = json.load(fh)
    feats, labels = [], []
    for ex in data["examples"]:
        feats.append(np.asarray(ex["Feature"], dtype=np.float32))
        labels.append(ex["Label"])
    return np.vstack(feats), labels, int(data["featureSize"])


def make_pairs(labels, rng, max_pairs=20000):
    by_label = {}
    for i, lab in enumerate(labels):
        by_label.setdefault(lab, []).append(i)
    pos = []
    for idxs in by_label.values():
        for a in range(len(idxs)):
            for b in range(a + 1, len(idxs)):
                pos.append((idxs[a], idxs[b]))
    rng.shuffle(pos)
    pos = pos[:max_pairs]
    n = len(labels)
    neg = []
    for _ in range(len(pos)):
        a, b = rng.integers(0, n), rng.integers(0, n)
        if labels[a] != labels[b]:
            neg.append((int(a), int(b)))
    return pos, neg


def normalize(m):
    norms = np.linalg.norm(m, axis=1, keepdims=True)
    norms[norms == 0] = 1.0
    return m / norms


def train(x, labels, dim, epochs, lr, seed):
    rng = np.random.default_rng(seed)
    f = x.shape[1]
    w = rng.normal(0, 0.1, size=(f, dim)).astype(np.float32)
    pos, neg = make_pairs(labels, rng)
    if not pos:
        print("WARN: no positive pairs (need repeated symbols across variants); model will be weak.",
              file=sys.stderr)
    margin = 0.3
    for epoch in range(epochs):
        emb = normalize(x @ w)
        grad = np.zeros_like(w)
        # Pull positives together.
        for a, b in pos:
            diff = emb[a] - emb[b]
            grad += np.outer(x[a] - x[b], diff) * (2.0 / max(len(pos), 1))
        # Push negatives apart up to a margin.
        for a, b in neg:
            d = np.linalg.norm(emb[a] - emb[b])
            if d < margin:
                diff = emb[a] - emb[b]
                grad -= np.outer(x[a] - x[b], diff) * (2.0 / max(len(neg), 1))
        w -= lr * grad
        if epoch % 20 == 0:
            print(f"epoch {epoch}: |W|={np.linalg.norm(w):.3f} pos={len(pos)} neg={len(neg)}")
    return w


def export_onnx(w, out_path):
    f, d = w.shape
    feature = helper.make_tensor_value_info("feature", TensorProto.FLOAT, [1, f])
    embedding = helper.make_tensor_value_info("embedding", TensorProto.FLOAT, [1, d])
    w_init = helper.make_tensor("W", TensorProto.FLOAT, [f, d], w.flatten().tolist())
    node = helper.make_node("MatMul", ["feature", "W"], ["embedding"])
    graph = helper.make_graph([node], "strata_sim", [feature], [embedding], [w_init])
    model = helper.make_model(graph, producer_name="strata-ml-training",
                              opset_imports=[helper.make_opsetid("", 13)])
    onnx.checker.check_model(model)
    onnx.save(model, out_path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--features", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--dim", type=int, default=32)
    ap.add_argument("--epochs", type=int, default=120)
    ap.add_argument("--lr", type=float, default=0.5)
    ap.add_argument("--seed", type=int, default=1)
    args = ap.parse_args()

    x, labels, fsize = load(args.features)
    print(f"loaded {x.shape[0]} examples, feature size {fsize}, unique labels {len(set(labels))}")
    w = train(x, labels, args.dim, args.epochs, args.lr, args.seed)
    export_onnx(w, args.out)
    print(f"exported ONNX model to {args.out} (feature {fsize} -> embedding {args.dim})")


if __name__ == "__main__":
    main()
