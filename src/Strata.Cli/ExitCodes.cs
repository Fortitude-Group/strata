namespace Strata.Cli;

/// <summary>Pipeline-gating exit codes (contracts/cli.md, FR-020).</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int Error = 1;
    public const int FindingsNeedAttention = 2;
    public const int Usage = 3;
}
