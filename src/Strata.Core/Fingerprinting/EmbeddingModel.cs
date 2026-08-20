using System;
using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Strata.Core.Fingerprinting;

/// <summary>
/// Wraps the optional learned function-similarity model (research.md R7). Maps an opcode-histogram
/// feature vector to an embedding via ONNX Runtime — the trainer is Python, inference ships in-process
/// here (no runtime install for the user). When no model is present the signal is simply absent
/// ("parked", SC-004); nothing else in the pipeline depends on it.
/// </summary>
public sealed class EmbeddingModel : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;

    private EmbeddingModel(InferenceSession session)
    {
        _session = session;
        _inputName = FirstKey(session.InputMetadata);
        _outputName = FirstKey(session.OutputMetadata);
    }

    public string Version { get; private init; } = "unknown";

    /// <summary>Loads a model from an .onnx file, or returns null if the path is missing/unloadable (parked).</summary>
    public static EmbeddingModel? TryLoad(string? onnxPath, string version = "unknown")
    {
        if (string.IsNullOrEmpty(onnxPath) || !System.IO.File.Exists(onnxPath))
        {
            return null;
        }

        try
        {
            return new EmbeddingModel(new InferenceSession(onnxPath)) { Version = version };
        }
        catch (Exception)
        {
            return null; // a broken/incompatible model must never abort a scan
        }
    }

    /// <summary>Embed one function's opcode histogram. Returns null on any inference failure.</summary>
    public float[]? Embed(float[] feature)
    {
        try
        {
            var tensor = new DenseTensor<float>(feature, [1, feature.Length]);
            var namedInputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(namedInputs);
            foreach (DisposableNamedOnnxValue r in results)
            {
                if (r.Name == _outputName)
                {
                    return [.. r.AsEnumerable<float>()];
                }
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose() => _session.Dispose();

    private static string FirstKey(IReadOnlyDictionary<string, NodeMetadata> metadata)
    {
        foreach (string key in metadata.Keys)
        {
            return key;
        }

        return string.Empty;
    }
}
