using System.Collections.Generic;
using Strata.Core.Util;

namespace Strata.Core.Fingerprinting;

/// <summary>
/// Signal (c): a MinHash over normalised instruction-sequence n-grams (research.md R5). Operands are
/// already abstracted away (mnemonics only), so the signal captures instruction-selection patterns
/// while tolerating register/immediate differences across compilers and optimisation levels.
/// </summary>
public static class NormInsnSignal
{
    public const int NGram = 4;

    public static IReadOnlyList<uint> Compute(IReadOnlyList<string> mnemonics)
    {
        var shingles = new List<ulong>();
        if (mnemonics.Count == 0)
        {
            return [];
        }

        // Pad short sequences so tiny functions still produce a signature.
        int n = System.Math.Min(NGram, mnemonics.Count);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i + n <= mnemonics.Count; i++)
        {
            sb.Clear();
            for (int k = 0; k < n; k++)
            {
                if (k > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(mnemonics[i + k]);
            }

            shingles.Add(Hashing.Fnv1a64(sb.ToString()));
        }

        if (shingles.Count == 0)
        {
            shingles.Add(Hashing.Fnv1a64(string.Join(' ', mnemonics)));
        }

        return MinHash.Compute(shingles);
    }
}
