using Strata.Core.Model;

namespace Strata.Core.Ingestion;

/// <summary>
/// Detects packed/obfuscated binaries (FR-005). Strata flags and reports — it never unpacks. Uses two
/// cheap heuristics: known packer signatures, and Shannon entropy of the payload (packed/encrypted
/// content approaches the ~8 bits/byte ceiling).
/// </summary>
public static class PackingDetector
{
    private static readonly byte[][] KnownSignatures =
    [
        "UPX!"u8.ToArray(),
        "UPX0"u8.ToArray(),
        "UPX1"u8.ToArray(),
        ".aspack"u8.ToArray(),
        "PEC2"u8.ToArray(),
    ];

    public static PackingStatus Detect(ReadOnlySpan<byte> data)
    {
        foreach (byte[] sig in KnownSignatures)
        {
            if (IndexOf(data, sig) >= 0)
            {
                return PackingStatus.Packed;
            }
        }

        double entropy = ShannonEntropy(data);
        return entropy switch
        {
            >= 7.5 => PackingStatus.Suspected,
            _ => PackingStatus.NotPacked,
        };
    }

    public static double ShannonEntropy(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return 0.0;
        }

        Span<int> counts = stackalloc int[256];
        foreach (byte b in data)
        {
            counts[b]++;
        }

        double entropy = 0.0;
        double len = data.Length;
        for (int i = 0; i < 256; i++)
        {
            if (counts[i] == 0)
            {
                continue;
            }

            double p = counts[i] / len;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return -1;
        }

        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }
}
