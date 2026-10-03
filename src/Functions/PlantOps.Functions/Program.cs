using Microsoft.Azure.Functions.Worker.Builder;
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

builder.Build().Run();
