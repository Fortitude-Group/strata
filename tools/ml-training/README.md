# Strata ML training (off ship path)

Trains the Checkpoint-B function-similarity model on corpus function-variant pairs and exports it to
**ONNX** (research.md R7). This is the **only** Python in the project and never runs inside the shipped
CLI — inference ships via ONNX Runtime bundled in the self-contained build.

**Gate (SC-004)**: the exported model ships only if it adds ≥5 points of recall over heuristics on the
benchmark; otherwise it is parked and Strata ships heuristics-first (tasks T071).

Status: scaffold only. Training pipeline is implemented in Phase 5 / US3 (tasks T069).
