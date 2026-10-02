using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PlantOps.Api.Health;
using PlantOps.Modules.Assets;
using PlantOps.Modules.Identity;
using PlantOps.Modules.Inventory;
using PlantOps.Modules.WorkOrders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sqlserver", tags: ["ready"]);

builder.Services
    .AddAssetsModule(builder.Configuration)
    .AddWorkOrdersModule(builder.Configuration)
    .AddInventoryModule(builder.Configuration)
    .AddIdentityModule(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Liveness runs no checks on purpose: a database outage must not make the orchestrator restart healthy instances.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync,
});

app.MapAssetsEndpoints()
    .MapWorkOrdersEndpoints()
    .MapInventoryEndpoints()
    .MapIdentityEndpoints();

// Unknown API routes must 404 as problem+json instead of falling through to the SPA's index.html.
// Specific routes beat this catch-all because it contains a catch-all parameter.
app.Map("/api/{**rest}", () => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound));

// UseStaticFiles rather than MapStaticAssets: MapStaticAssets relies on a build-time manifest, and the
// Dockerfile copies the Angular build into wwwroot after publish, so those files would never be listed.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
