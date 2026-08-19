namespace Strata.Core;

/// <summary>Static build/version facts about the engine.</summary>
public static class StrataInfo
{
    /// <summary>Tool version, embedded in every <see cref="Model.ScanResult"/> and SBOM.</summary>
    public const string Version = "0.1.0";

    /// <summary>Signature-DB schema version this build understands (contracts/signature-db.md).</summary>
    public const int SupportedCorpusSchemaVersion = 1;
}
