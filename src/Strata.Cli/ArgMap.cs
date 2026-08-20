using System.Globalization;

namespace Strata.Cli;

/// <summary>
/// Minimal non-interactive argument parser for the CLI (FR-020). Supports one positional argument plus
/// `--flag` and `--key value` / `--key=value` options. (A richer parser lands with the full CLI, T049.)
/// </summary>
public sealed class ArgMap
{
    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

    public string? Positional { get; private set; }

    public static ArgMap Parse(IReadOnlyList<string> args, int startIndex)
    {
        var map = new ArgMap();
        for (int i = startIndex; i < args.Count; i++)
        {
            string a = args[i];
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                string key = a[2..];
                int eq = key.IndexOf('=', StringComparison.Ordinal);
                if (eq >= 0)
                {
                    map._options[key[..eq]] = key[(eq + 1)..];
                }
                else if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    map._options[key] = args[++i];
                }
                else
                {
                    map._flags.Add(key);
                }
            }
            else if (map.Positional is null)
            {
                map.Positional = a;
            }
        }

        return map;
    }

    public string? Get(string key) => _options.TryGetValue(key, out string? v) ? v : null;

    public bool Has(string key) => _flags.Contains(key) || _options.ContainsKey(key);

    public double GetDouble(string key, double fallback) =>
        _options.TryGetValue(key, out string? v)
        && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? d
            : fallback;
}
