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

// Unknown API routes must 404 as problem+json instead of falling through to the SPA's index.html.
// Specific routes beat this catch-all because it contains a catch-all parameter.
// Anonymous on purpose: the fallback policy would otherwise answer 401 and hide the 404 (and nothing leaks:
// every real route is still protected).
app.Map("/api/{**rest}", () => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound)).AllowAnonymous();

// The SPA's own files are public; the API behind them is what needs a session.
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

public partial class Program;
