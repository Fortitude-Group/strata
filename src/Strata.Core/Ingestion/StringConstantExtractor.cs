using System.Collections.Generic;
using System.Text;
using Strata.Core.Model;

namespace Strata.Core.Ingestion;

/// <summary>
/// Extracts printable ASCII string literals from a binary (FR-003), the substrate for the
/// string/constant fingerprint signal (research.md R5 signal a). Works across the whole file so it is
/// robust to stripped/odd section layouts — the pragmatic equivalent of the classic `strings` tool.
/// </summary>
public static class StringConstantExtractor
{
    public static IReadOnlyList<StringLiteral> Extract(ReadOnlySpan<byte> data, int minLength)
    {
        var results = new List<StringLiteral>();
        int runStart = -1;
        var sb = new StringBuilder();

        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            bool printable = b is >= 0x20 and <= 0x7E;
            if (printable)
            {
                if (runStart < 0)
                {
                    runStart = i;
                }

                sb.Append((char)b);
            }
            else
            {
                Flush(results, sb, runStart, minLength);
                runStart = -1;
            }
        }

        Flush(results, sb, runStart, minLength);
        return results;
    }

    private static void Flush(List<StringLiteral> results, StringBuilder sb, int runStart, int minLength)
    {
        if (runStart >= 0 && sb.Length >= minLength)
        {
            results.Add(new StringLiteral(sb.ToString(), (ulong)runStart));
        }

        sb.Clear();
    }
}
