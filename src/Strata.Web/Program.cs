using System.Threading.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Strata.Web.Components;
using Strata.Web.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.SectionName));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ScanDemoService>();
builder.Services.AddSingleton<PerIpScanThrottle>();
builder.Services.AddScoped<ClientCircuitContext>();

// HTTP-layer abuse control (FR-025, US6 sc.4): throttles page loads / circuit negotiation per
// remote IP. The expensive Scan() call itself is additionally throttled per-client inside
// PerIpScanThrottle, since Blazor Server invokes it over a persistent SignalR connection rather
// than discrete HTTP requests that this middleware can see.
// Behind a reverse proxy / CDN, honour X-Forwarded-For so the rate limiter and throttle key on the real
// client IP rather than the proxy's (which would collapse all users into one bucket). SECURITY: only
// trust these headers from known proxies — set KnownProxies/KnownNetworks at deploy; unset means the
// headers are ignored (safe default), so this is inert until a proxy is explicitly trusted.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
});

builder.Services.AddRateLimiter(limiterOptions =>
{
    limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        string key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseRateLimiter();

// The bundled samples use a .dat extension (deliberately, to dodge *.bin/*.elf .gitignore rules —
// see wwwroot/samples) which ASP.NET Core's default content-type map doesn't recognise; without
// this, UseStaticFiles() would silently skip serving them (ServeUnknownFileTypes defaults to
// false). The demo itself reads samples straight off disk (ScanDemoService), so this only matters
// for direct inspection of the sample files over HTTP.
var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".dat"] = "application/octet-stream";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypeProvider });
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
