using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using PlantOps.Api.Health;
using PlantOps.Api.Http;
using PlantOps.Api.Security;
using PlantOps.Api.Seeding;
using PlantOps.Modules.Assets;
using PlantOps.Modules.Identity;
using PlantOps.Modules.Inventory;
using PlantOps.Modules.Reporting;
using PlantOps.Modules.WorkOrders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Modules add their own readiness checks (tagged "ready") when registered.
builder.Services.AddHealthChecks();

builder.Services
    .AddAssetsModule(builder.Configuration)
    .AddWorkOrdersModule(builder.Configuration)
    .AddInventoryModule(builder.Configuration)
    .AddReportingModule(builder.Configuration)
    .AddIdentityModule(builder.Configuration);

// Registered after the modules on purpose: hosted services start in registration order, so every module's
// migrations have run before the demo seed (Seed:Demo=true) looks at the database.
builder.Services.AddDemoSeed();
builder.Services.AddPlantOpsRateLimiting();

// Read when the options are first used, not now, so settings added after registration (tests) are honoured.
builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // Azure App Service terminates TLS in its front end and calls us over plain HTTP from addresses we cannot list, so
    // without X-Forwarded-Proto the app believes the request is http (the Secure __Host- cookie, the OIDC redirect URI
    // and HSTS would all go wrong) and without X-Forwarded-For every client looks like the front end (the login rate
    // limit would be one shared bucket). The default only trusts loopback proxies, so the platform must opt in: ONLY
    // when ForwardedHeaders:TrustAll=true, which the App Service deployment sets and nothing else does. Trusting these
    // headers from anyone would let a client forge its IP and scheme, so the default stays closed.
    if (configuration.GetValue<bool>("ForwardedHeaders:TrustAll"))
    {
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

var app = builder.Build();

// First: everything after it (rate limiting, HTTPS-aware cookies, logging) must see the real client address and scheme.
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

// UseStaticFiles rather than MapStaticAssets: MapStaticAssets relies on a build-time manifest, and the
// Dockerfile copies the Angular build into wwwroot after publish, so those files would never be listed.
// Before authentication so the SPA's files stay anonymous without per-file endpoint metadata.
app.UseDefaultFiles();
app.UseStaticFiles();
// The rate limiter sits between authentication and authorization: it needs the user to partition by, and it must also
// count the requests that authorization is about to answer with 401/403.
app.UseIdentityModule(pipeline => pipeline.UseRateLimiter());

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

// Liveness runs no checks on purpose: a database outage must not make the orchestrator restart healthy instances.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync,
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync,
}).AllowAnonymous();

app.MapAssetsEndpoints()
    .MapWorkOrdersEndpoints()
    .MapInventoryEndpoints()
    .MapReportingEndpoints()
    .MapIdentityEndpoints();

// The SPA fallback must not swallow /api: an unknown API path is a 404 problem, never index.html with 200.
// /api is excluded from the fallback route rather than answered by an "/api/{**rest}" catch-all: a catch-all is
// itself a candidate endpoint and masked the real answer (a JSON endpoint called without a body came back as a
// misleading 404 instead of the endpoint's 400). With no endpoint matched: anonymous callers get 401 (the
// fallback policy also covers "no endpoint", so route existence is not probeable anonymously) and signed-in
// callers get routing's 404, turned into problem+json by UseStatusCodePages.
// The SPA's own files are public; the API behind them is what needs a session.
app.MapFallbackToFile("{*path:nonfile:regex(^(?!api(/|$)))}", "index.html").AllowAnonymous();

app.Run();

public partial class Program;
