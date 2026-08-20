namespace Strata.Web.Services;

/// <summary>
/// Deploy-time config for the public web demo (contracts/web-demo.md "Config parameters"). Retention
/// is deliberately NOT a setting here — it is always "none / in-memory" (contract invariant), not
/// configurable.
/// </summary>
public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    /// <summary>Hard upload cap (FR-025). Over-cap uploads are rejected before any scan runs.</summary>
    public long MaxUploadBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Scan requests a single client may make within <see cref="ThrottleWindowSeconds"/> (FR-025, US6 sc.4).</summary>
    public int ThrottlePerIp { get; init; } = 10;

    /// <summary>Window length, in seconds, for <see cref="ThrottlePerIp"/>.</summary>
    public int ThrottleWindowSeconds { get; init; } = 60;
}
