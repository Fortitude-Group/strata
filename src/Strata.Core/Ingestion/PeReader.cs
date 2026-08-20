using System.Buffers.Binary;
using System.Collections.Generic;
using Strata.Core.Model;

namespace Strata.Core.Ingestion;

/// <summary>
/// Minimal, tolerant PE/COFF reader (FR-001, Phase 10). Reads architecture, entry point, and the
/// section table (with executable-section detection) — enough to drive string extraction and x86-64
/// function recovery. Best-effort: a malformed table degrades to header-only facts.
/// </summary>
public static class PeReader
{
    private const uint ImageScnMemExecute = 0x20000000;

    public sealed record PeInfo(Architecture Architecture, ulong Entry, IReadOnlyList<Section> Sections);

    public static PeInfo Read(ReadOnlySpan<byte> data)
    {
        var sections = new List<Section>();
        try
        {
            int peOffset = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x3C, 4));
            int coff = peOffset + 4;
            ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(coff, 2));
            ushort numSections = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(coff + 2, 2));
            ushort optSize = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(coff + 16, 2));

            Architecture arch = machine switch
            {
                0x8664 => Architecture.X86_64,
                0xAA64 => Architecture.AArch64,
                _ => Architecture.Other,
            };

            int optHeader = coff + 20;
            ulong entry = optSize >= 20 ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(optHeader + 16, 4)) : 0;

            int sectionTable = optHeader + optSize;
            for (int i = 0; i < numSections; i++)
            {
                int baseOff = sectionTable + (i * 40);
                if (baseOff + 40 > data.Length)
                {
                    break;
                }

                string name = ReadName(data.Slice(baseOff, 8));
                uint virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(baseOff + 8, 4));
                uint virtualAddr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(baseOff + 12, 4));
                uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(baseOff + 16, 4));
                uint rawPtr = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(baseOff + 20, 4));
                uint characteristics = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(baseOff + 36, 4));

                // Only executable sections carry code we want to recover functions from.
                if ((characteristics & ImageScnMemExecute) != 0 || name == ".text")
                {
                    sections.Add(new Section(name, rawPtr, System.Math.Min(rawSize, virtualSize == 0 ? rawSize : virtualSize), virtualAddr));
                }
            }

            return new PeInfo(arch, entry, sections);
        }
        catch (System.Exception)
        {
            return new PeInfo(Architecture.Unknown, 0, sections);
        }
    }

    private static string ReadName(ReadOnlySpan<byte> raw)
    {
        int len = 0;
        while (len < raw.Length && raw[len] != 0)
        {
            len++;
        }

        return System.Text.Encoding.ASCII.GetString(raw[..len]);
    }
}
