using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Strata.Web.Tests;

/// <summary>
/// Minimal <see cref="IWebHostEnvironment"/> stand-in so <c>ScanDemoService</c> can be exercised without
/// spinning up a full ASP.NET Core host (T089 — unit-test what's testable without a full web host).
/// Points <see cref="WebRootPath"/> at the real Strata.Web/wwwroot so bundled-sample tests read the
/// actual shipped fixtures.
/// </summary>
internal sealed class FakeWebHostEnvironment : IWebHostEnvironment
{
    public string WebRootPath { get; set; } = string.Empty;

    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

    public string EnvironmentName { get; set; } = "Testing";

    public string ApplicationName { get; set; } = "Strata.Web.Tests";

    public string ContentRootPath { get; set; } = string.Empty;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
