using System.Buffers.Binary;
using System.Collections.Generic;
using Strata.Core.Model;

namespace Strata.Core.Ingestion;

/// <summary>
/// Minimal, tolerant ELF reader (FR-001, x86-64 + AArch64 first). Reads only what Strata needs —
/// architecture, linkage hint, section table, entry point (research.md R3). Parsing is best-effort:
/// a malformed section table degrades to header-only facts rather than failing the scan, because
/// stripped/odd binaries are the norm (FR-002).
/// </summary>
public static class ElfReader
{
    private const int EiClass = 4;   // 1 = 32-bit, 2 = 64-bit
    private const int EiData = 5;    // 1 = little-endian, 2 = big-endian

    public sealed record ElfHeader(bool Is64, bool IsLittleEndian, Architecture Architecture, ulong Entry, bool IsSharedObject);

    public static Architecture MachineToArchitecture(ushort eMachine) => eMachine switch
    {
        0x3E => Architecture.X86_64,   // EM_X86_64
        0xB7 => Architecture.AArch64,  // EM_AARCH64
        _ => Architecture.Other,
    };

    public static ElfHeader ReadHeader(ReadOnlySpan<byte> data)
    {
        bool is64 = data.Length > EiClass && data[EiClass] == 2;
        bool le = data.Length <= EiData || data[EiData] != 2;

        ushort eType = 0;
        ushort eMachine = 0;
        ulong entry = 0;

        // e_type @16 (2), e_machine @18 (2). e_entry @24 (8 for ELF64, 4 for ELF32).
        if (data.Length >= 20)
        {
            eType = ReadU16(data.Slice(16, 2), le);
            eMachine = ReadU16(data.Slice(18, 2), le);
        }

        if (is64 && data.Length >= 32)
        {
            entry = ReadU64(data.Slice(24, 8), le);
        }
        else if (!is64 && data.Length >= 28)
        {
            entry = ReadU32(data.Slice(24, 4), le);
        }

        bool isSharedObject = eType == 3; // ET_DYN
        return new ElfHeader(is64, le, MachineToArchitecture(eMachine), entry, isSharedObject);
    }

    /// <summary>Best-effort section-header parse. Returns an empty list if the table is unreadable.</summary>
    public static IReadOnlyList<Section> ReadSections(ReadOnlySpan<byte> data, ElfHeader header)
    {
        var sections = new List<Section>();
        try
        {
            if (!header.Is64 || data.Length < 64)
            {
                return sections; // 32-bit section parsing not needed for the x86-64/AArch64-first scope.
            }

            bool le = header.IsLittleEndian;
            ulong shoff = ReadU64(data.Slice(40, 8), le);   // e_shoff
            ushort shentsize = ReadU16(data.Slice(58, 2), le);
            ushort shnum = ReadU16(data.Slice(60, 2), le);
            ushort shstrndx = ReadU16(data.Slice(62, 2), le);

            if (shoff == 0 || shnum == 0 || shentsize < 64 || shoff + ((ulong)shnum * shentsize) > (ulong)data.Length)
            {
                return sections;
            }

            // Locate the section-header string table.
            ulong shstrOffEntry = shoff + ((ulong)shstrndx * shentsize);
            ulong shstrOff = ReadU64(data.Slice((int)shstrOffEntry + 24, 8), le);
            ulong shstrSize = ReadU64(data.Slice((int)shstrOffEntry + 32, 8), le);

            for (ushort i = 0; i < shnum; i++)
            {
                int baseOff = (int)(shoff + ((ulong)i * shentsize));
                uint nameIdx = ReadU32(data.Slice(baseOff, 4), le);
                ulong addr = ReadU64(data.Slice(baseOff + 16, 8), le);
                ulong offset = ReadU64(data.Slice(baseOff + 24, 8), le);
                ulong size = ReadU64(data.Slice(baseOff + 32, 8), le);

                string name = ReadCString(data, (int)(shstrOff + nameIdx), (int)shstrSize);
                sections.Add(new Section(name, offset, size, addr));
            }
        }
        catch (System.Exception)
        {
            // Tolerant by design: a corrupt section table must not fail the whole scan.
            return sections;
        }

        return sections;
    }

    private static string ReadCString(ReadOnlySpan<byte> data, int start, int maxLen)
    {
        if (start < 0 || start >= data.Length)
        {
            return string.Empty;
        }

        int end = start;
        int limit = System.Math.Min(data.Length, start + System.Math.Max(0, maxLen));
        while (end < limit && data[end] != 0)
        {
            end++;
        }

        return System.Text.Encoding.ASCII.GetString(data[start..end]);
    }

    private static ushort ReadU16(ReadOnlySpan<byte> s, bool le) =>
        le ? BinaryPrimitives.ReadUInt16LittleEndian(s) : BinaryPrimitives.ReadUInt16BigEndian(s);

    private static uint ReadU32(ReadOnlySpan<byte> s, bool le) =>
        le ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s);

    private static ulong ReadU64(ReadOnlySpan<byte> s, bool le) =>
        le ? BinaryPrimitives.ReadUInt64LittleEndian(s) : BinaryPrimitives.ReadUInt64BigEndian(s);
}
