using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Strata.Core.Diagnostics;

/// <summary>
/// Minimal structured (JSON-lines) telemetry sink (Principle IV — observable behaviour). Off by default
/// (null writer); when a writer is attached, the engine emits one JSON object per meaningful operation
/// with a stage, a message, and structured fields (e.g. per-stage timings), so a consumer can trace a
/// scan. Deterministic scan output is unaffected — telemetry is a side channel, never part of the SBOM.
/// </summary>
public sealed class StructuredLog
{
    private readonly TextWriter? _writer;

    public StructuredLog(TextWriter? writer) => _writer = writer;

    /// <summary>A no-op log (the default): nothing is written.</summary>
    public static StructuredLog Null { get; } = new(null);

    public bool IsEnabled => _writer is not null;

    public void Event(string stage, string message, IReadOnlyDictionary<string, object>? fields = null)
    {
        if (_writer is null)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.Append("{\"stage\":").Append(Quote(stage));
        sb.Append(",\"message\":").Append(Quote(message));
        if (fields is not null)
        {
            foreach ((string key, object value) in fields)
            {
                sb.Append(',').Append(Quote(key)).Append(':').Append(Format(value));
            }
        }

        sb.Append('}');
        _writer.WriteLine(sb.ToString());
    }

    private static string Format(object value) => value switch
    {
        int or long or double or float => System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
        bool b => b ? "true" : "false",
        _ => Quote(value.ToString() ?? string.Empty),
    };

    private static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
