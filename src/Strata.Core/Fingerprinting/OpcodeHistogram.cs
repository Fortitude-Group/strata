using System.Collections.Generic;

namespace Strata.Core.Fingerprinting;

/// <summary>
/// A fixed opcode vocabulary and the normalised mnemonic-histogram feature the learned-embedding model
/// consumes (research.md R7). The vocabulary is frozen so the feature vector is stable across the
/// trainer (Python) and the inference runtime (Core) — the model's input contract.
/// </summary>
public static class OpcodeHistogram
{
    /// <summary>Frozen vocabulary of common x86-64 + AArch64 mnemonics (index = feature position).</summary>
    public static readonly IReadOnlyList<string> Vocabulary =
    [
        // x86-64
        "mov", "push", "pop", "call", "ret", "lea", "add", "sub", "xor", "and",
        "or", "cmp", "test", "jmp", "je", "jne", "jz", "jnz", "jg", "jl",
        "jle", "jge", "ja", "jb", "shl", "shr", "sar", "imul", "mul", "div",
        "inc", "dec", "neg", "not", "movzx", "movsx", "nop", "leave", "cdqe", "cqo",
        // AArch64
        "ldr", "str", "ldp", "stp", "b", "bl", "blr", "br", "cbz", "cbnz",
        "adrp", "movz", "movk", "orr", "eor", "ands", "csel", "cset", "madd", "svc",
    ];

    public static int Size => Vocabulary.Count;

    private static readonly Dictionary<string, int> Index = BuildIndex();

    /// <summary>Normalised (L1) histogram of the given mnemonics over the frozen vocabulary.</summary>
    public static float[] Compute(IReadOnlyList<string> mnemonics)
    {
        var counts = new float[Size];
        int total = 0;
        foreach (string m in mnemonics)
        {
            if (Index.TryGetValue(m.ToLowerInvariant(), out int idx))
            {
                counts[idx]++;
                total++;
            }
        }

        if (total > 0)
        {
            for (int i = 0; i < counts.Length; i++)
            {
                counts[i] /= total;
            }
        }

        return counts;
    }

    private static Dictionary<string, int> BuildIndex()
    {
        var map = new Dictionary<string, int>(System.StringComparer.Ordinal);
        for (int i = 0; i < Vocabulary.Count; i++)
        {
            map[Vocabulary[i]] = i;
        }

        return map;
    }
}
