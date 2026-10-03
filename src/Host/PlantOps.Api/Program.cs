using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PlantOps.Api.Health;
using PlantOps.Api.Http;
using PlantOps.Modules.Assets;
using PlantOps.Modules.Identity;
using PlantOps.Modules.Inventory;
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
    .AddIdentityModule(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// UseStaticFiles rather than MapStaticAssets: MapStaticAssets relies on a build-time manifest, and the
// Dockerfile copies the Angular build into wwwroot after publish, so those files would never be listed.
// Before authentication so the SPA's files stay anonymous without per-file endpoint metadata.
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseIdentityModule();

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
