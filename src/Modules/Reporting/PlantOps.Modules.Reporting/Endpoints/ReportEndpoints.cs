using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Identity.Contracts;
using PlantOps.Modules.Reporting.Export;
using PlantOps.Modules.Reporting.Projection;
using PlantOps.Modules.Reporting.Queries;

namespace PlantOps.Modules.Reporting.Endpoints;

internal static class ReportEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/mttr", Mttr).RequireAuthorization(Policies.ViewReports);
        group.MapGet("/sla-compliance", Sla).RequireAuthorization(Policies.ViewReports);
        group.MapGet("/downtime", Downtime).RequireAuthorization(Policies.ViewReports);
        group.MapGet("/export.xlsx", Export).RequireAuthorization(Policies.ViewReports);
        group.MapPost("/rebuild", Rebuild).RequireAuthorization(Policies.RebuildReports);
    }

    private static async Task<Ok<ReportResponse<MttrRow>>> Mttr(
        DateOnly? from, DateOnly? to, string? groupBy, FactoryClock clock, ReportQueries queries, CancellationToken ct)
    {
        var grouping = ReportGrouping.Parse(groupBy, MttrGroup.Line);
        var range = ReportRange.Resolve(from, to, clock.Today, clock.Zone);
        var rows = await queries.MttrAsync(range, grouping, ct);
        return TypedResults.Ok(new ReportResponse<MttrRow>(range.From, range.To, ReportGrouping.Name(grouping), rows));
    }

    private static async Task<Ok<ReportResponse<SlaRow>>> Sla(
        DateOnly? from, DateOnly? to, string? groupBy, FactoryClock clock, ReportQueries queries, CancellationToken ct)
    {
        var grouping = ReportGrouping.Parse(groupBy, SlaGroup.Priority);
        var range = ReportRange.Resolve(from, to, clock.Today, clock.Zone);
        var rows = await queries.SlaAsync(range, grouping, ct);
        return TypedResults.Ok(new ReportResponse<SlaRow>(range.From, range.To, ReportGrouping.Name(grouping), rows));
    }

    private static async Task<Ok<ReportResponse<DowntimeRow>>> Downtime(
        DateOnly? from, DateOnly? to, string? groupBy, FactoryClock clock, ReportQueries queries, CancellationToken ct)
    {
        var grouping = ReportGrouping.Parse(groupBy, DowntimeGroup.Line);
        var range = ReportRange.Resolve(from, to, clock.Today, clock.Zone);
        var rows = await queries.DowntimeAsync(range, grouping, ct);
        return TypedResults.Ok(new ReportResponse<DowntimeRow>(range.From, range.To, ReportGrouping.Name(grouping), rows));
    }

    private static async Task<FileContentHttpResult> Export(
        DateOnly? from, DateOnly? to, FactoryClock clock, ReportQueries queries, CancellationToken ct)
    {
        var range = ReportRange.Resolve(from, to, clock.Today, clock.Zone);

        // Sequential on purpose: the queries share one DbContext, which allows one command at a time.
        var mttrByLine = await queries.MttrAsync(range, MttrGroup.Line, ct);
        var slaByPriority = await queries.SlaAsync(range, SlaGroup.Priority, ct);
        var downtimeByLine = await queries.DowntimeAsync(range, DowntimeGroup.Line, ct);
        var mttrByMonth = await queries.MttrAsync(range, MttrGroup.Month, ct);

        using var workbook = ReportWorkbook.Build(mttrByLine, slaByPriority, downtimeByLine, mttrByMonth);

        // The xlsx is a zip, and zip writing seeks, which the response stream cannot do. The workbook is a few KB, so it
        // is built in memory (no temp file on disk) and handed to the framework, which sends it with
        // "Content-Disposition: attachment; filename=...".
        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        return TypedResults.File(buffer.ToArray(), ReportWorkbook.ContentType, ReportWorkbook.FileName(range.From, range.To));
    }

    private static async Task<Ok<RebuildResponse>> Rebuild(ReportRebuilder rebuilder, CancellationToken ct) =>
        TypedResults.Ok(new RebuildResponse(await rebuilder.RebuildAsync(ct)));

    private sealed record RebuildResponse(int Projected);
}
