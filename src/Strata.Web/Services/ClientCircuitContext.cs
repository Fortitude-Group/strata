namespace Strata.Web.Services;

/// <summary>
/// Captures the client's remote IP once, at the point the Blazor Server circuit is established. That is
/// the only moment an <c>HttpContext</c> is available in a scoped service for an interactive-server
/// component — once the circuit is running over SignalR, ordinary requests are gone. Used to key the
/// per-client scan throttle (FR-025, US6 sc.4).
/// </summary>
public sealed class ClientCircuitContext
{
    public ClientCircuitContext(IHttpContextAccessor accessor)
    {
        RemoteIp = accessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    public string RemoteIp { get; }
}
