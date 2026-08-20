using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Strata.Core.Model;
using Strata.Core.Util;

namespace Strata.Core.Fingerprinting;

/// <summary>
/// Signal (b): a hash of the control-flow-graph *shape* — block count, edge count, and the sorted
/// in/out degree sequences (research.md R5). Address- and register-independent, so it is stable across
/// compilers' register allocation and layout choices.
/// </summary>
public static class CfgShapeSignal
{
    public static ulong Compute(RecoveredFunction function)
    {
        int blocks = function.BasicBlocks.Count;
        var outDeg = new int[blocks];
        var inDeg = new int[blocks];
        foreach ((int from, int to) in function.Edges)
        {
            if (from >= 0 && from < blocks)
            {
                outDeg[from]++;
            }

            if (to >= 0 && to < blocks)
            {
                inDeg[to]++;
            }
        }

        var sb = new StringBuilder();
        sb.Append("b=").Append(blocks.ToString(CultureInfo.InvariantCulture));
        sb.Append(";e=").Append(function.Edges.Count.ToString(CultureInfo.InvariantCulture));
        sb.Append(";o=").Append(string.Join(',', outDeg.OrderBy(x => x)));
        sb.Append(";i=").Append(string.Join(',', inDeg.OrderBy(x => x)));
        return Hashing.Fnv1a64(sb.ToString());
    }
}
