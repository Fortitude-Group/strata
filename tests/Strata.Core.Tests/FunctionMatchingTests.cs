using Strata.Core.Corpus;
using Strata.Core.Disassembly;
using Strata.Core.Fingerprinting;
using Strata.Core.Matching;
using Strata.Core.Model;

namespace Strata.Core.Tests;

public sealed class FunctionMatchingTests
{
    // A small real x86-64 function with a conditional branch (=> multiple basic blocks):
    //   push rbp; mov rbp,rsp; test edi,edi; je +2; xor eax,eax; mov eax,edi; pop rbp; ret
    private static readonly byte[] FuncA =
        [0x55, 0x48, 0x89, 0xE5, 0x85, 0xFF, 0x74, 0x02, 0x31, 0xC0, 0x89, 0xF8, 0x5D, 0xC3];

    //   xor eax,eax; inc eax; inc eax; ret  (structurally different, no branch)
    private static readonly byte[] FuncB = [0x31, 0xC0, 0xFF, 0xC0, 0xFF, 0xC0, 0xC3];

    private static TargetFunction Fingerprint(byte[] code, int id, ulong baseAddr)
    {
        IReadOnlyList<DecodedInstruction> instrs = new IcedX64Decoder().Decode(code, baseAddr);
        (IReadOnlyList<BasicBlock> blocks, IReadOnlyList<(int, int)> edges) = Recovery.CfgBuilder.Build(instrs);
        var fn = new RecoveredFunction
        {
            Id = id,
            StartAddress = baseAddr,
            EndAddress = baseAddr + (ulong)code.Length,
            BasicBlocks = blocks,
            Edges = edges,
            Mnemonics = instrs.Select(i => i.Mnemonic).ToList(),
        };
        return new TargetFunction(fn, new Fingerprinter().Fingerprint(fn, Placeholder()));
    }

    private static ScanTarget Placeholder() => new()
    {
        Name = "t",
        SizeBytes = 0,
        Format = BinaryFormat.Elf,
        Architecture = Architecture.X86_64,
    };

    private static ICorpus CorpusWith(TargetFunction fn, string library, string version) => new InMemoryCorpus(
        new CorpusManifest { CorpusVersion = "fn-test", SchemaVersion = 1, LibraryCount = 1 },
        signatures: [],
        functionSignatures:
        [
            new CorpusFunctionSignature
            {
                LibraryName = library,
                FunctionName = "target_fn",
                CfgShapeHash = fn.Signature.CfgShapeHash,
                NormInsnMinHash = fn.Signature.NormInsnMinHash,
                Distinctiveness = 1.0,
                ExactVersion = version,
            },
        ]);

    [Fact]
    public void Iced_decodes_and_cfg_has_multiple_blocks_for_branching_function()
    {
        TargetFunction a = Fingerprint(FuncA, 0, 0x1000);
        Assert.True(a.Function.BasicBlocks.Count >= 2);          // the je split the block
        Assert.Contains("Push", a.Function.Mnemonics);
        Assert.NotEmpty(a.Signature.NormInsnMinHash);
    }

    [Fact]
    public void Identical_function_matches_its_corpus_signature()
    {
        TargetFunction corpusFn = Fingerprint(FuncA, 0, 0x2000);
        ICorpus corpus = CorpusWith(corpusFn, "libfoo", "1.0.0");

        TargetFunction targetFn = Fingerprint(FuncA, 0, 0x9000);   // same code, different address
        IReadOnlyList<LibraryFunctionEvidence> ev = new FunctionEvidenceMatcher().Match([targetFn], corpus);

        LibraryFunctionEvidence foo = Assert.Single(ev);
        Assert.Equal("libfoo", foo.LibraryName);
        Assert.NotEmpty(foo.Evidence);                            // FR-014
        Assert.True(foo.Coverage > 0);
    }

    [Fact]
    public void Different_function_does_not_match()
    {
        TargetFunction corpusFn = Fingerprint(FuncA, 0, 0x2000);
        ICorpus corpus = CorpusWith(corpusFn, "libfoo", "1.0.0");

        TargetFunction other = Fingerprint(FuncB, 0, 0x9000);
        IReadOnlyList<LibraryFunctionEvidence> ev = new FunctionEvidenceMatcher().Match([other], corpus);

        Assert.Empty(ev);                                         // no false positive
    }

    [Fact]
    public void Composite_matcher_reports_unmatched_function_as_unidentified_region()
    {
        TargetFunction corpusFn = Fingerprint(FuncA, 0, 0x2000);
        ICorpus corpus = CorpusWith(corpusFn, "libfoo", "1.0.0");

        TargetFunction matched = Fingerprint(FuncA, 1, 0x9000);
        TargetFunction unmatched = Fingerprint(FuncB, 2, 0xA000);

        ScanResult result = new CompositeMatcher().Match(
            Placeholder(), [matched, unmatched], corpus, new MatchOptions(), "test");

        Assert.Contains(result.Components, c => c.LibraryName == "libfoo");
        Assert.Contains(result.UnidentifiedRegions, r => r.FunctionIds.Contains(2));  // SC-008 precise region
        Assert.DoesNotContain(result.UnidentifiedRegions, r => r.FunctionIds.Contains(1));
    }
}
