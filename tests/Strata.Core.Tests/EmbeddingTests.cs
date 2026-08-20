using Strata.Core.Fingerprinting;
using Xunit;

namespace Strata.Core.Tests;

public sealed class EmbeddingTests
{
    [Fact]
    public void Opcode_histogram_is_normalised_over_the_frozen_vocabulary()
    {
        float[] h = OpcodeHistogram.Compute(["mov", "mov", "ret", "unknown_mnemonic"]);

        Assert.Equal(OpcodeHistogram.Size, h.Length);
        // "unknown_mnemonic" is outside the vocabulary and ignored; mov,mov,ret -> 3 counted.
        float sum = 0;
        foreach (float v in h)
        {
            sum += v;
        }

        Assert.Equal(1.0f, sum, 3);   // L1-normalised
    }

    [Fact]
    public void Missing_model_loads_as_parked_null()
    {
        // A non-existent path parks the signal rather than throwing (SC-004 graceful absence).
        Assert.Null(EmbeddingModel.TryLoad("does-not-exist.onnx"));
        Assert.Null(EmbeddingModel.TryLoad(null));
    }

    [Fact]
    public void Fingerprinter_without_model_produces_no_embedding()
    {
        var fn = new Strata.Core.Model.RecoveredFunction { Id = 0, StartAddress = 0, EndAddress = 4, Mnemonics = ["mov", "ret"] };
        var target = new Strata.Core.Model.ScanTarget
        {
            Name = "t", SizeBytes = 0, Format = Strata.Core.Model.BinaryFormat.Elf, Architecture = Strata.Core.Model.Architecture.X86_64,
        };

        Strata.Core.Model.FunctionSignature sig = new Fingerprinter().Fingerprint(fn, target);
        Assert.Null(sig.Embedding);
    }
}
