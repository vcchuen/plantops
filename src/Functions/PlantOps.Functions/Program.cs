using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Functions;
using PlantOps.Modules.WorkOrders;

// A second host of the same modular monolith (ADR-0010): it composes only the WorkOrders module, because the
// runners need only the WorkOrders database. No Assets/Identity/Inventory: the runners work from the snapshots
// stored on work orders and schedules (ADR-0002), so nothing here calls another module.
var builder = FunctionsApplication.CreateBuilder(args);

// The audit interceptor needs an ICurrentUser; in the API the Identity module supplies it from the HTTP request.
// A timer has no user, so every change the runners make is recorded as "System".
builder.Services.AddSingleton<ICurrentUser, NoCurrentUser>();

// Settings arrive as app settings: ConnectionStrings__PlantOps, Factory__TimeZone, ServiceBus__*.
// This also registers the outbox dispatcher, so breach events written by the escalation runner are forwarded to
// Service Bus from this process too (and from the API's dispatcher: rows are claimed with READPAST, never twice at once).
builder.Services.AddWorkOrdersModule(builder.Configuration);

builder.Services.AddHttpClient();

// OpenTelemetry -> Application Insights, only when the platform provides the connection string (Azure sets it from
// the Bicep app settings; local runs and tests have none, so nothing is exported and no Azure call is made).
// Documented approach for the isolated worker: UseFunctionsWorkerDefaults() + UseAzureMonitorExporter(), and
// "telemetryMode": "OpenTelemetry" in host.json so the host's own telemetry flows through the same pipeline.
// https://learn.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide#opentelemetry
// The exporter reads APPLICATIONINSIGHTS_CONNECTION_STRING itself.
//
// Service Bus trigger auth: Connection = "ServiceBus" (SlaBreachNotifier) is a setting-name PREFIX. Identity-based
// access therefore needs the app setting ServiceBus__fullyQualifiedNamespace = <ns>.servicebus.windows.net plus the
// "Azure Service Bus Data Receiver" role on the function app's managed identity. No connection string is stored.
// https://learn.microsoft.com/azure/azure-functions/functions-reference#configure-an-identity-based-connection
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

builder.Build().Run();
