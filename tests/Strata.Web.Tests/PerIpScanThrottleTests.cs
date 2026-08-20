using Microsoft.Extensions.Options;
using Strata.Web.Services;

namespace Strata.Web.Tests;

/// <summary>T089: <see cref="PerIpScanThrottle"/> allows exactly N acquisitions per client within the
/// configured window, then blocks — the abuse control FR-025/US6 sc.4 relies on.</summary>
public sealed class PerIpScanThrottleTests
{
    private static PerIpScanThrottle MakeThrottle(int permitLimit, int windowSeconds = 60) =>
        new(Options.Create(new DemoOptions { ThrottlePerIp = permitLimit, ThrottleWindowSeconds = windowSeconds }));

    [Fact]
    public void Allows_up_to_the_configured_limit_then_blocks()
    {
        using PerIpScanThrottle throttle = MakeThrottle(permitLimit: 3, windowSeconds: 300);
        const string client = "203.0.113.7";

        Assert.True(throttle.TryAcquire(client));
        Assert.True(throttle.TryAcquire(client));
        Assert.True(throttle.TryAcquire(client));
        Assert.False(throttle.TryAcquire(client));   // 4th request in the window is blocked
    }

    [Fact]
    public void Different_clients_are_tracked_independently()
    {
        using PerIpScanThrottle throttle = MakeThrottle(permitLimit: 1, windowSeconds: 300);

        Assert.True(throttle.TryAcquire("client-a"));
        Assert.False(throttle.TryAcquire("client-a"));   // client-a exhausted its one permit

        Assert.True(throttle.TryAcquire("client-b"));    // client-b has its own independent budget
    }

    [Fact]
    public void Single_permit_client_is_blocked_on_its_second_attempt()
    {
        // Boundary at the opposite end from the "up to N" case above: the smallest valid budget (1)
        // still enforces the same allow-then-block shape (FixedWindowRateLimiter requires PermitLimit
        // > 0, so 1 — not 0 — is the true lower boundary for ThrottlePerIp).
        using PerIpScanThrottle throttle = MakeThrottle(permitLimit: 1, windowSeconds: 300);
        const string client = "198.51.100.4";

        Assert.True(throttle.TryAcquire(client));
        Assert.False(throttle.TryAcquire(client));
    }
}
