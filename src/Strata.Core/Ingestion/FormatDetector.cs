using Strata.Core.Model;

namespace Strata.Core.Ingestion;

/// <summary>
/// Detects the container format and architecture from magic bytes / headers (FR-001, FR-003).
/// Deliberately tolerant: it identifies the format even when later structural parsing is best-effort,
/// because stripped binaries are the default case (FR-002).
/// </summary>
public static class FormatDetector
{
    public static BinaryFormat DetectFormat(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 4 && data[0] == 0x7F && data[1] == (byte)'E' && data[2] == (byte)'L' && data[3] == (byte)'F')
        {
            return BinaryFormat.Elf;
        }

        // PE: "MZ" DOS stub, with a PE signature pointed to at offset 0x3C.
        if (data.Length >= 0x40 && data[0] == (byte)'M' && data[1] == (byte)'Z')
        {
            int peOffset = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x3C, 4));
            if (peOffset > 0 && peOffset + 4 <= data.Length &&
                data[peOffset] == (byte)'P' && data[peOffset + 1] == (byte)'E' &&
                data[peOffset + 2] == 0 && data[peOffset + 3] == 0)
            {
                return BinaryFormat.Pe;
            }
        }

        if (data.Length >= 4)
        {
            uint magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data[..4]);

            // Mach-O 32/64 (both endiannesses) and fat/universal.
            if (magic is 0xFEEDFACE or 0xFEEDFACF or 0xCEFAEDFE or 0xCFFAEDFE or 0xCAFEBABE or 0xBEBAFECA)
            {
                return BinaryFormat.MachO;
            }
        }

        return BinaryFormat.Unknown;
    }
}
