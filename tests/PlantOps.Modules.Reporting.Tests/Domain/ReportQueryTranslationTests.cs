using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Reporting.Infrastructure;
using PlantOps.Modules.Reporting.Queries;

namespace PlantOps.Modules.Reporting.Tests.Domain;

/// <summary>
/// Proves the aggregation happens in SQL without a database: the context is pointed at a server that does not exist,
/// opening the connection is suppressed, and an interceptor captures the command text just before it would run.
/// If a LINQ shape stopped translating, EF would throw (or silently fetch rows and group in memory, which would
/// show up here as a SELECT with no GROUP BY).
/// </summary>
public class ReportQueryTranslationTests
{
    private static readonly ReportRange Range = ReportRange.Resolve(new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), new DateOnly(2026, 10, 3), FactoryClock.FindZone(null));

    private static async Task<string> SqlOf(Func<ReportQueries, Task> run)
    {
        var capture = new CaptureSql();
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseSqlServer("Server=tcp:nowhere.invalid;Database=none;User Id=x;Password=x;TrustServerCertificate=true")
            .AddInterceptors(capture, new SuppressOpen())
            .Options;
        await using var db = new ReportingDbContext(options);

        await Assert.ThrowsAsync<SqlCaptured>(() => run(new ReportQueries(db)));
        return capture.Sql ?? throw new InvalidOperationException("No SQL was captured.");
    }

    [Fact]
    public async Task Mttr_by_line_averages_and_counts_in_sql()
    {
        var sql = await SqlOf(q => q.MttrAsync(Range, MttrGroup.Line, default));

        Assert.Contains("AVG(", sql);
        Assert.Contains("COUNT(*)", sql);
        Assert.Contains("GROUP BY", sql);
        Assert.Contains("[LineId]", sql);
        Assert.Contains("[CompletedAt] >=", sql);
        Assert.Contains("[CompletedAt] <", sql);
    }

    [Fact]
    public async Task Mttr_by_asset_groups_on_the_asset_columns()
    {
        var sql = await SqlOf(q => q.MttrAsync(Range, MttrGroup.Asset, default));

        Assert.Contains("GROUP BY", sql);
        Assert.Contains("[AssetId]", sql);
        Assert.Contains("[AssetTag]", sql);
    }

    [Fact]
    public async Task Mttr_by_month_groups_on_the_stored_month_column()
    {
        var sql = await SqlOf(q => q.MttrAsync(Range, MttrGroup.Month, default));

        Assert.Contains("GROUP BY", sql);
        Assert.Contains("[CompletedMonth]", sql);
    }

    [Fact]
    public async Task Sla_counts_the_met_rows_in_sql()
    {
        var sql = await SqlOf(q => q.SlaAsync(Range, SlaGroup.Priority, default));

        Assert.Contains("GROUP BY", sql);
        Assert.Contains("[Priority]", sql);
        Assert.Contains("[MetSla]", sql);
        Assert.Contains("COUNT(*)", sql);
    }

    [Fact]
    public async Task Downtime_sums_only_rows_with_downtime_in_sql()
    {
        var sql = await SqlOf(q => q.DowntimeAsync(Range, DowntimeGroup.Line, default));

        Assert.Contains("SUM(", sql);
        Assert.Contains("[DowntimeMinutes] IS NOT NULL", sql);
        Assert.Contains("GROUP BY", sql);
    }

    [Fact]
    public async Task Downtime_by_month_translates_too()
    {
        var sql = await SqlOf(q => q.DowntimeAsync(Range, DowntimeGroup.Month, default));

        Assert.Contains("GROUP BY", sql);
        Assert.Contains("[CompletedMonth]", sql);
    }

    private sealed class SqlCaptured : Exception;

    private sealed class SuppressOpen : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            new(InterceptionResult.Suppress());
    }

    private sealed class CaptureSql : DbCommandInterceptor
    {
        public string? Sql { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Sql = command.CommandText;
            throw new SqlCaptured();
        }
    }
}
