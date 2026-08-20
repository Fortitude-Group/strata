using Strata.Core.Errors;
using Strata.Core.Ingestion;

namespace Strata.Core.Tests;

public sealed class BinaryLoaderTests
{
    [Fact]
    public void Load_throws_out_of_envelope_when_input_exceeds_max_bytes()
    {
        byte[] elf = new byte[128];
        elf[0] = 0x7F; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
        elf[4] = 2; elf[5] = 1;

        var loader = new BinaryLoader();
        var options = new LoadOptions { MaxInputBytes = 64 };

        using var ms = new MemoryStream(elf);
        OutOfEnvelopeException ex = Assert.Throws<OutOfEnvelopeException>(
            () => loader.Load(ms, "oversize.elf", options));

        Assert.Contains("128", ex.Message, StringComparison.Ordinal);
        Assert.Contains("64", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_accepts_input_within_the_configured_envelope()
    {
        byte[] elf = new byte[64];
        elf[0] = 0x7F; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
        elf[4] = 2; elf[5] = 1;

        var loader = new BinaryLoader();
        var options = new LoadOptions { MaxInputBytes = 1024 };

        using var ms = new MemoryStream(elf);
        Strata.Core.Model.ScanTarget target = loader.Load(ms, "ok.elf", options);

        Assert.Equal(64, target.SizeBytes);
    }

    [Fact]
    public void Zero_max_bytes_means_no_limit()
    {
        byte[] elf = new byte[64];
        elf[0] = 0x7F; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
        elf[4] = 2; elf[5] = 1;

        var loader = new BinaryLoader();
        var options = new LoadOptions { MaxInputBytes = 0 };

        using var ms = new MemoryStream(elf);
        Strata.Core.Model.ScanTarget target = loader.Load(ms, "ok.elf", options);

        Assert.Equal(64, target.SizeBytes);
    }
}
