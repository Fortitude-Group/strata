using System.Buffers.Binary;
using System.Collections.Generic;
using Strata.Core.Model;

namespace Strata.Core.Ingestion;

/// <summary>
/// Minimal, tolerant Mach-O reader (FR-001, Phase 10). Handles 64-bit Mach-O (both endiannesses),
/// reading architecture and the __TEXT segment's executable sections for string extraction and x86-64
/// function recovery. Fat/universal binaries and 32-bit are detected as Mach-O but parsed best-effort.
/// </summary>
public static class MachOReader
{
    // Values obtained by reading the 4 magic bytes big-endian:
    //   a little-endian 64-bit Mach-O stores CF FA ED FE  -> 0xCFFAEDFE
    //   a big-endian    64-bit Mach-O stores FE ED FA CF  -> 0xFEEDFACF
    private const uint Read64LeFile = 0xCFFAEDFE;
    private const uint Read64BeFile = 0xFEEDFACF;
    private const uint LcSegment64 = 0x19;

    public sealed record MachOInfo(Architecture Architecture, IReadOnlyList<Section> Sections);

    public static MachOInfo Read(ReadOnlySpan<byte> data)
    {
        var sections = new List<Section>();
        try
        {
            uint magic = BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
            bool is64 = magic is Read64LeFile or Read64BeFile;
            bool le = magic == Read64LeFile;
            if (!is64)
            {
                return new MachOInfo(Architecture.Other, sections); // 32-bit / fat: detected, header-only
            }

            uint cpuType = ReadU32(data.Slice(4, 4), le);
            Architecture arch = (cpuType & 0x00FFFFFF) switch
            {
                0x07 => Architecture.X86_64,   // CPU_TYPE_X86 | ABI64
                0x0C => Architecture.AArch64,  // CPU_TYPE_ARM | ABI64
                _ => Architecture.Other,
            };

            uint ncmds = ReadU32(data.Slice(16, 4), le);
            int offset = 32; // 64-bit Mach-O header size
            for (uint c = 0; c < ncmds && offset + 8 <= data.Length; c++)
            {
                uint cmd = ReadU32(data.Slice(offset, 4), le);
                uint cmdSize = ReadU32(data.Slice(offset + 4, 4), le);
                if (cmdSize == 0)
                {
                    break;
                }

                if (cmd == LcSegment64)
                {
                    ReadSegmentSections(data, offset, le, sections);
                }

                offset += (int)cmdSize;
            }

            return new MachOInfo(arch, sections);
        }
        catch (System.Exception)
        {
            return new MachOInfo(Architecture.Unknown, sections);
        }
    }

    private static void ReadSegmentSections(ReadOnlySpan<byte> data, int segOffset, bool le, List<Section> sections)
    {
        // segment_command_64: cmd(4) cmdsize(4) segname(16) vmaddr(8) vmsize(8) fileoff(8) filesize(8)
        //   maxprot(4) initprot(4) nsects(4) flags(4) => sections follow at segOffset+72
        uint nsects = ReadU32(data.Slice(segOffset + 64, 4), le);
        int sectOff = segOffset + 72;
        for (uint s = 0; s < nsects; s++)
        {
            int baseOff = sectOff + ((int)s * 80); // section_64 is 80 bytes
            if (baseOff + 80 > data.Length)
            {
                break;
            }

            string sectName = ReadFixedString(data.Slice(baseOff, 16));
            ulong addr = ReadU64(data.Slice(baseOff + 32, 8), le);
            ulong size = ReadU64(data.Slice(baseOff + 40, 8), le);
            uint fileOffset = ReadU32(data.Slice(baseOff + 48, 4), le);
            sections.Add(new Section(sectName, fileOffset, size, addr));
        }
    }

    private static string ReadFixedString(ReadOnlySpan<byte> raw)
    {
        int len = 0;
        while (len < raw.Length && raw[len] != 0)
        {
            len++;
        }

        return System.Text.Encoding.ASCII.GetString(raw[..len]);
    }

    private static uint ReadU32(ReadOnlySpan<byte> s, bool le) =>
        le ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s);

    private static ulong ReadU64(ReadOnlySpan<byte> s, bool le) =>
        le ? BinaryPrimitives.ReadUInt64LittleEndian(s) : BinaryPrimitives.ReadUInt64BigEndian(s);
}
