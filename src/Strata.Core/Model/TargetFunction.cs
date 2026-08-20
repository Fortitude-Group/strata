namespace Strata.Core.Model;

/// <summary>A recovered target function paired with its computed fingerprint, ready for matching.</summary>
public sealed record TargetFunction(RecoveredFunction Function, FunctionSignature Signature);
