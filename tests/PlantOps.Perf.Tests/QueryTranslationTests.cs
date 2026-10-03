using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PlantOps.Modules.Assets.Domain;
using PlantOps.Modules.Assets.Infrastructure;
using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.Modules.WorkOrders.Infrastructure;

namespace PlantOps.Perf.Tests;

/// <summary>
/// The harness's first real run is in CI, so everything checkable offline is checked here: the experiment queries
/// translate to the SQL the experiments claim to measure (ToQueryString needs no connection; the compiled query is
/// captured with a connection that never opens).
/// </summary>
public class QueryTranslationTests
{
    private const string Fake = "Server=tcp:nowhere.invalid;Database=none;User Id=x;Password=x;TrustServerCertificate=true";

    private static WorkOrdersDbContext WorkOrders(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<WorkOrdersDbContext>().UseSqlServer(Fake).AddInterceptors(interceptors).Options);

    [Fact]
    public void The_page_projection_is_one_query_over_the_snapshot_columns_without_joins()
    {
        using var db = WorkOrders();

        var sql = NPlusOneExperiment.PageProjection(db, 0).ToQueryString();

        Assert.Contains("[AssetName]", sql);
        Assert.Contains("[AssignedToName]", sql);
        Assert.Contains("FROM [workorders].[WorkOrders]", sql);
        Assert.Contains("ORDER BY [w].[SubmittedAt] DESC, [w].[Number] DESC", sql);
        Assert.Contains("OFFSET", sql);
        Assert.DoesNotContain("JOIN", sql);
    }

    [Fact]
    public void The_naive_page_loads_whole_entities()
    {
        using var db = WorkOrders();

        var sql = NPlusOneExperiment.PageOfEntities(db, 0).ToQueryString();

        Assert.Contains("[Description]", sql);
        Assert.Contains("ORDER BY [w].[SubmittedAt] DESC, [w].[Number] DESC", sql);
    }

    [Fact]
    public void The_per_row_lookup_asks_the_assets_table_for_one_name_by_id()
    {
        using var db = new AssetsDbContext(new DbContextOptionsBuilder<AssetsDbContext>().UseSqlServer(Fake).Options);

        var sql = NPlusOneExperiment.AssetName(db, new AssetId(Guid.NewGuid())).ToQueryString();

        Assert.Contains("[a].[Name]", sql);
        Assert.Contains("FROM [assets].[Assets]", sql);
        Assert.Contains("[a].[Id] = @", sql);
    }

    [Fact]
    public async Task The_compiled_detail_query_translates_to_a_lookup_by_key()
    {
        var capture = new CaptureSql();
        await using var db = WorkOrders(capture, new SuppressOpen());

        await Assert.ThrowsAsync<SqlCaptured>(() => DetailQueries.CompiledById(db, new WorkOrderId(Guid.NewGuid())));

        Assert.Contains("FROM [workorders].[WorkOrders]", capture.Sql);
        Assert.Contains("[w].[Id] = @", capture.Sql);
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
