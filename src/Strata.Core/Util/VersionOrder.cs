using System.Globalization;

namespace Strata.Core.Util;

/// <summary>
/// Orders dotted version strings (numeric segments plus an optional trailing letter suffix, e.g.
/// <c>1.1.1k</c>). Used to bound versions honestly (FR-013) — never comparing versions as raw strings,
/// where "1.2.11" would sort before "1.2.9".
/// </summary>
public static class VersionOrder
{
    public static int Compare(string a, string b)
    {
        if (a == "unknown" || b == "unknown")
        {
            return string.CompareOrdinal(a, b);
        }

        (int[] aNums, string aSuffix) = Parse(a);
        (int[] bNums, string bSuffix) = Parse(b);

        int len = Math.Max(aNums.Length, bNums.Length);
        for (int i = 0; i < len; i++)
        {
            int av = i < aNums.Length ? aNums[i] : 0;
            int bv = i < bNums.Length ? bNums[i] : 0;
            if (av != bv)
            {
                return av.CompareTo(bv);
            }
        }

        return string.CompareOrdinal(aSuffix, bSuffix);
    }

    public static string Min(string a, string b) => Compare(a, b) <= 0 ? a : b;

    public static string Max(string a, string b) => Compare(a, b) >= 0 ? a : b;

    private static (int[] Nums, string Suffix) Parse(string version)
    {
        string suffix = string.Empty;
        int cut = version.Length;
        for (int i = 0; i < version.Length; i++)
        {
            char c = version[i];
            if (!char.IsDigit(c) && c != '.')
            {
                suffix = version[i..];
                cut = i;
                break;
            }
        }

        string numeric = version[..cut];
        string[] parts = numeric.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var nums = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            nums[i] = int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
        }

        return (nums, suffix);
    }
}
