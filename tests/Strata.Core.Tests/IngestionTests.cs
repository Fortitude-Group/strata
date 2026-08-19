using System.Text;
using Strata.Core.Ingestion;
using Strata.Core.Model;
using Xunit;

namespace Strata.Core.Tests;

public sealed class IngestionTests
{
    [Fact]
    public void Detects_elf_from_magic()
    {
        byte[] elf = [0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0];
        Assert.Equal(BinaryFormat.Elf, FormatDetector.DetectFormat(elf));
    }

    [Fact]
    public void Rejects_non_binary_as_unknown()
    {
        byte[] text = Encoding.ASCII.GetBytes("this is just a text file, not a binary");
        Assert.Equal(BinaryFormat.Unknown, FormatDetector.DetectFormat(text));
    }

    [Fact]
    public void Extracts_printable_strings_above_min_length()
    {
        byte[] data = [0x00, 0x01, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0x00, (byte)'a', (byte)'b'];
        var strings = StringConstantExtractor.Extract(data, minLength: 5);

        Assert.Contains(strings, s => s.Value == "hello");
        Assert.DoesNotContain(strings, s => s.Value == "ab");   // below min length
    }

    [Fact]
    public void Flags_upx_packed_binary()
    {
        byte[] data = Encoding.ASCII.GetBytes("....UPX!....some packed payload....");
        Assert.Equal(PackingStatus.Packed, PackingDetector.Detect(data));
    }

    [Fact]
    public void Reads_elf_architecture_from_header()
    {
        byte[] elf = new byte[64];
        elf[0] = 0x7F; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
        elf[4] = 2; // 64-bit
        elf[5] = 1; // little-endian
        elf[18] = 0x3E; // e_machine = EM_X86_64

        ElfReader.ElfHeader header = ElfReader.ReadHeader(elf);
        Assert.Equal(Architecture.X86_64, header.Architecture);
        Assert.True(header.Is64);
    }
}
