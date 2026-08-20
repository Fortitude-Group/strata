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
}
