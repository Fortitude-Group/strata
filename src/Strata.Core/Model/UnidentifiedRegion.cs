using System.Collections.Generic;

namespace Strata.Core.Model;

/// <summary>
/// A portion of the target not attributed to any corpus library, reported for honesty (FR-015, SC-008).
/// Every recovered function is either inside a component's evidence or accounted for here — no silent
/// drops, no forced matches.
/// </summary>
public sealed record UnidentifiedRegion
{
    public required ulong StartAddress { get; init; }

    public required ulong EndAddress { get; init; }

    public required UnidentifiedReason Reason { get; init; }

    public IReadOnlyList<int> FunctionIds { get; init; } = [];
}
