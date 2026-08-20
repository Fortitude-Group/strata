using Strata.Core.Model;

namespace Strata.Core.Fingerprinting;

/// <summary>
/// Default <see cref="IFingerprinter"/> — combines the heuristic per-function signals (FR-007). The same
/// code builds the corpus and fingerprints scan targets (Principle I), so a target function and its
/// corpus counterpart are compared on identical features. When an <see cref="EmbeddingModel"/> is
/// supplied, the learned-embedding signal (T070) is added; when it is absent (parked, SC-004) the
/// heuristic signals stand alone.
/// </summary>
public sealed class Fingerprinter : IFingerprinter
{
    private readonly EmbeddingModel? _model;

    public Fingerprinter(EmbeddingModel? model = null) => _model = model;

    public FunctionSignature Fingerprint(RecoveredFunction function, ScanTarget target)
    {
        float[]? embedding = _model?.Embed(OpcodeHistogram.Compute(function.Mnemonics));
        return new FunctionSignature
        {
            FunctionId = function.Id,
            CfgShapeHash = CfgShapeSignal.Compute(function),
            NormInsnMinHash = NormInsnSignal.Compute(function.Mnemonics),
            StringConstRefs = [],
            Embedding = embedding,
        };
    }
}
