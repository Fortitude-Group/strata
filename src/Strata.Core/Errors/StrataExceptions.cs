namespace Strata.Core.Errors;

/// <summary>Base type for all Strata engine errors.</summary>
public abstract class StrataException : Exception
{
    protected StrataException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Input is not a supported binary format (corrupt, truncated, or unrecognised) — FR-004.</summary>
public sealed class UnsupportedFormatException : StrataException
{
    public UnsupportedFormatException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The corpus DB schema version is not supported by this tool version — signature-db contract.</summary>
public sealed class CorpusSchemaMismatchException : StrataException
{
    public CorpusSchemaMismatchException(int found, int supported)
        : base($"Corpus schema version {found} is not supported by this build (supports {supported}).")
    {
        Found = found;
        Supported = supported;
    }

    public int Found { get; }

    public int Supported { get; }
}

/// <summary>The input is beyond the supported size envelope — reported, never hung (engine-api contract).</summary>
public sealed class OutOfEnvelopeException : StrataException
{
    public OutOfEnvelopeException(string message) : base(message) { }
}
