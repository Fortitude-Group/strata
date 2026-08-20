using Strata.Core.Model;

namespace Strata.Core.Fingerprinting;

/// <summary>
/// Default <see cref="IFingerprinter"/> — combines the heuristic per-function signals (FR-007). The same
/// code builds the corpus and fingerprints scan targets (Principle I), so a target function and its
/// corpus counterpart are compared on identical features. The learned embedding signal (T070) attaches
/// here behind the SC-004 gate.
/// </summary>
public sealed class Fingerprinter : IFingerprinter
{
    public FunctionSignature Fingerprint(RecoveredFunction function, ScanTarget target) => new()
    {
        FunctionId = function.Id,
        CfgShapeHash = CfgShapeSignal.Compute(function),
        NormInsnMinHash = NormInsnSignal.Compute(function.Mnemonics),
        StringConstRefs = [],
        Embedding = null,
    };
}
