using Strata.Core.Ingestion;
using Strata.Core.Model;

namespace Strata.Core.Tests;

public sealed class PackingDetectorEntropyTests
{
    [Fact]
    public void Entropy_of_all_same_byte_data_is_zero()
    {
        byte[] data = new byte[256];   // every byte is 0x00
        Assert.Equal(0.0, PackingDetector.ShannonEntropy(data));
    }

    [Fact]
    public void Entropy_of_empty_data_is_zero()
    {
        Assert.Equal(0.0, PackingDetector.ShannonEntropy(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Entropy_of_uniformly_distributed_bytes_is_near_the_eight_bit_ceiling()
    {
        // Each of the 256 possible byte values occurs exactly once -> maximal entropy (log2(256) == 8).
        byte[] data = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            data[i] = (byte)i;
        }

        double entropy = PackingDetector.ShannonEntropy(data);
        Assert.True(entropy > 7.99, $"expected near-maximal entropy, got {entropy}");
    }

    [Fact]
    public void Low_entropy_data_without_a_packer_signature_is_not_packed()
    {
        byte[] data = new byte[512];   // all zero -> zero entropy, no known signature
        Assert.Equal(PackingStatus.NotPacked, PackingDetector.Detect(data));
    }

    [Fact]
    public void High_entropy_data_without_a_known_signature_is_suspected()
    {
        byte[] data = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            data[i] = (byte)i;   // maximal entropy, no packer magic present
        }

        Assert.Equal(PackingStatus.Suspected, PackingDetector.Detect(data));
    }

    [Fact]
    public void Aspack_signature_is_detected_as_packed()
    {
        // ".aspack" (with leading dot) is the exact signature PackingDetector looks for.
        byte[] header = System.Text.Encoding.ASCII.GetBytes("....some header bytes....");
        byte[] sig = ".aspack"u8.ToArray();
        byte[] tail = System.Text.Encoding.ASCII.GetBytes("....payload....");
        byte[] combined = [.. header, .. sig, .. tail];

        Assert.Equal(PackingStatus.Packed, PackingDetector.Detect(combined));
    }
}
