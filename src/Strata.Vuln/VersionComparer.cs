using System;
using System.Collections.Generic;
using System.Globalization;

namespace Strata.Vuln;

/// <summary>
/// Compares dotted numeric version strings with an optional trailing letter suffix on any segment
/// (e.g. OpenSSL's <c>1.1.1k</c>). Each dot-separated segment is split into a numeric prefix and a
/// letter suffix; segments compare numerically first, then the suffix compares ordinally (no suffix
/// sorts before any lettered suffix, so <c>1.1.1</c> &lt; <c>1.1.1a</c> &lt; <c>1.1.1k</c>). Missing
/// trailing segments are treated as <c>0</c> with no suffix.
/// </summary>
internal static class VersionComparer
{
    public static int Compare(string a, string b)
    {
        ArgumentException.ThrowIfNullOrEmpty(a);
        ArgumentException.ThrowIfNullOrEmpty(b);

        List<(int Number, string Suffix)> segmentsA = ParseSegments(a);
        List<(int Number, string Suffix)> segmentsB = ParseSegments(b);
        int count = Math.Max(segmentsA.Count, segmentsB.Count);

        for (int i = 0; i < count; i++)
        {
            (int numA, string suffixA) = i < segmentsA.Count ? segmentsA[i] : (0, string.Empty);
            (int numB, string suffixB) = i < segmentsB.Count ? segmentsB[i] : (0, string.Empty);

            if (numA != numB)
            {
                return numA.CompareTo(numB);
            }

            int suffixCompare = string.CompareOrdinal(suffixA, suffixB);
            if (suffixCompare != 0)
            {
                return suffixCompare;
            }
        }

        return 0;
    }

    private static List<(int Number, string Suffix)> ParseSegments(string version)
    {
        string[] parts = version.Split('.');
        var segments = new List<(int, string)>(parts.Length);

        foreach (string part in parts)
        {
            int i = 0;
            while (i < part.Length && char.IsDigit(part[i]))
            {
                i++;
            }

            string numberPart = part[..i];
            string suffix = part[i..];
            int number = numberPart.Length > 0 ? int.Parse(numberPart, CultureInfo.InvariantCulture) : 0;
            segments.Add((number, suffix));
        }

        return segments;
    }
}
