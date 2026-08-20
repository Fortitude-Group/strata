using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Strata.Web.Services;

/// <summary>
/// Per-client scan throttle (FR-025, US6 sc.4): abuse control without login. The ASP.NET Core HTTP-level
/// rate limiter registered in Program.cs only sees discrete HTTP requests (page loads, circuit
/// negotiation) — a Blazor Server circuit keeps one persistent SignalR connection per user, so repeated
/// clicks of "Scan" never generate new HTTP requests for that middleware to see. This guards the
/// expensive <see cref="Strata.Core.IScanner.Scan"/> call itself, keyed by <see cref="ClientCircuitContext.RemoteIp"/>.
/// </summary>
public sealed class PerIpScanThrottle : IDisposable
{
    private readonly ConcurrentDictionary<string, FixedWindowRateLimiter> _limiters = new(StringComparer.Ordinal);
    private readonly DemoOptions _options;

    public PerIpScanThrottle(IOptions<DemoOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Attempts to consume one scan permit for <paramref name="clientKey"/>. Never blocks.</summary>
    public bool TryAcquire(string clientKey)
    {
        FixedWindowRateLimiter limiter = _limiters.GetOrAdd(clientKey, _ => new FixedWindowRateLimiter(
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = _options.ThrottlePerIp,
                Window = TimeSpan.FromSeconds(_options.ThrottleWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

        using RateLimitLease lease = limiter.AttemptAcquire();
        return lease.IsAcquired;
    }

    public void Dispose()
    {
        foreach (FixedWindowRateLimiter limiter in _limiters.Values)
        {
            limiter.Dispose();
        }
    }
}
