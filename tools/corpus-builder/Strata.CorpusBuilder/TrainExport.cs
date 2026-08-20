using System.Text.Json;
using Strata.Core;
using Strata.Core.Fingerprinting;
using Strata.Core.Ingestion;
using Strata.Core.Model;
using Strata.Core.Recovery;

namespace Strata.CorpusBuilder;

/// <summary>
/// Exports labelled training examples for the learned-embedding model (research.md R7, T069). Each
/// example is one recovered function's opcode-histogram feature plus its symbol name (the label) and the
/// build variant it came from — so the Python trainer can form positive pairs (same symbol, different
/// compiler/opt) and negatives. Only functions the (non-stripped) corpus builds give a symbol for are
/// exported; that symbol identity is the supervision signal.
/// </summary>
public static class TrainExport
{
    private sealed record TrainExample(string Label, string Variant, float[] Feature);

    public static int Export(string binariesDir, string outPath)
    {
        var loader = new BinaryLoader();
        var recovery = new FunctionRecovery();
        var examples = new List<TrainExample>();

        foreach (string binaryPath in Directory.EnumerateFiles(binariesDir).OrderBy(p => p, StringComparer.Ordinal))
        {
            string variant = Path.GetFileNameWithoutExtension(binaryPath);
            ScanTarget target;
            using (FileStream fs = File.OpenRead(binaryPath))
            {
                target = loader.Load(fs, variant, new LoadOptions());
            }

            var symbolByAddress = new Dictionary<ulong, string>();
            foreach (Symbol s in target.Symbols)
            {
                symbolByAddress.TryAdd(s.Address, s.Name);
            }

            foreach (RecoveredFunction fn in recovery.Recover(target, new RecoveryOptions()))
            {
                if (!symbolByAddress.TryGetValue(fn.StartAddress, out string? label) || fn.Mnemonics.Count == 0)
                {
                    continue; // only symbol-labelled functions supervise training
                }

                examples.Add(new TrainExample(label, variant, OpcodeHistogram.Compute(fn.Mnemonics)));
            }
        }

        File.WriteAllText(outPath, JsonSerializer.Serialize(new
        {
            featureSize = OpcodeHistogram.Size,
            vocabulary = OpcodeHistogram.Vocabulary,
            examples,
        }));

        return examples.Count;
    }
}
