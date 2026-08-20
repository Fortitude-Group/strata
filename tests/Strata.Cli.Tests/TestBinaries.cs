using System.Text;

namespace Strata.Cli.Tests;

/// <summary>Builds tiny, deterministic fixture binaries of known composition for integration tests.</summary>
internal static class TestBinaries
{
    /// <summary>
    /// A minimal valid ELF64 (x86-64) header followed by the given strings. The string signal scans the
    /// whole file, so appended strings are found; e_shoff is 0 so section parsing cleanly yields none.
    /// </summary>
    public static byte[] MakeElfWithStrings(IEnumerable<string> strings)
    {
        var bytes = new List<byte>(new byte[64]);
        bytes[0] = 0x7F; bytes[1] = (byte)'E'; bytes[2] = (byte)'L'; bytes[3] = (byte)'F';
        bytes[4] = 2;    // EI_CLASS = 64-bit
        bytes[5] = 1;    // EI_DATA  = little-endian
        bytes[6] = 1;    // EI_VERSION
        bytes[16] = 2;   // e_type = ET_EXEC
        bytes[18] = 0x3E; // e_machine = EM_X86_64
        bytes[20] = 1;   // e_version
        // e_entry @24
        bytes[24] = 0x00; bytes[25] = 0x10; bytes[26] = 0x40; bytes[27] = 0x00;
        // e_shoff @40 stays 0 -> no section table
        bytes[52] = 64;  // e_ehsize

        foreach (string s in strings)
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(s));
            bytes.Add(0x00);
        }

        return bytes.ToArray();
    }

    /// <summary>
    /// A minimal valid PE (x86-64) header followed by the given strings. Mirrors the layout
    /// <c>Strata.Core.Tests.IngestionTests.Detects_and_reads_pe_x86_64</c> exercises: 'MZ' at 0, the PE
    /// header offset at 0x3C pointing to 0x80, "PE\0\0" there, machine = IMAGE_FILE_MACHINE_AMD64 at
    /// 0x84, and SizeOfOptionalHeader at 0x94. No section table (numSections stays 0), so the string
    /// signal — which scans the whole file — is what identifies the appended strings.
    /// </summary>
    public static byte[] MakePeWithStrings(IEnumerable<string> strings)
    {
        var bytes = new List<byte>(new byte[512]);
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        bytes[0x3C] = 0x80;                                 // PE header offset
        bytes[0x80] = (byte)'P'; bytes[0x81] = (byte)'E';   // "PE\0\0" (bytes 0x82/0x83 already 0)
        bytes[0x84] = 0x64; bytes[0x85] = 0x86;             // machine = IMAGE_FILE_MACHINE_AMD64 (0x8664 LE)
        bytes[0x94] = 0xE0;                                 // SizeOfOptionalHeader

        foreach (string s in strings)
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(s));
            bytes.Add(0x00);
        }

        return bytes.ToArray();
    }

    /// <summary>
    /// A minimal valid 64-bit little-endian Mach-O header followed by the given strings. Mirrors
    /// <c>Strata.Core.Tests.IngestionTests.Detects_and_reads_macho_x86_64</c>: magic <c>CF FA ED FE</c>,
    /// cputype <c>07 00 00 01</c> (CPU_TYPE_X86_64, little-endian) at offset 4. ncmds stays 0, so no
    /// segment/section parsing is attempted — the string signal finds the appended strings regardless.
    /// </summary>
    public static byte[] MakeMachOWithStrings(IEnumerable<string> strings)
    {
        var bytes = new List<byte>(new byte[64]);
        bytes[0] = 0xCF; bytes[1] = 0xFA; bytes[2] = 0xED; bytes[3] = 0xFE;   // 64-bit LE magic on disk
        bytes[4] = 0x07; bytes[5] = 0x00; bytes[6] = 0x00; bytes[7] = 0x01;  // cputype = CPU_TYPE_X86_64 (LE)

        foreach (string s in strings)
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(s));
            bytes.Add(0x00);
        }

        return bytes.ToArray();
    }
}
