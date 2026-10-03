using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PlantOps.Modules.Assets.Infrastructure;
using PlantOps.Modules.Reporting.Infrastructure;
using PlantOps.Modules.WorkOrders.Infrastructure;

namespace PlantOps.Perf;

/// <summary>
/// The measured database: the real module DbContexts, the real migrations, bulk-loaded seed data.
/// The harness builds the contexts directly instead of booting the Api host (WebApplicationFactory): the host adds
/// OIDC, hosted services (outbox, SLA jobs) and test authentication that would write to the tables while they are
/// being measured, and none of it is under test. What matters here is the model, the migrations and the SQL Server
/// provider, and those are exactly the production ones. The only duplicated line is the migrations-history setting
/// that each module's AddXxxModule passes to UseSqlServer.
/// </summary>
internal sealed class PerfDatabase(string connectionString)
{
    public string ConnectionString { get; } = connectionString;

    public DbContextOptions<TContext> Options<TContext>(string schema, params IInterceptor[] interceptors)
        where TContext : DbContext => new DbContextOptionsBuilder<TContext>()
            .UseSqlServer(ConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", schema))
            .AddInterceptors(interceptors)
            .Options;

    public AssetsDbContext Assets(params IInterceptor[] interceptors) => new(Options<AssetsDbContext>(AssetsDbContext.Schema, interceptors));

    public WorkOrdersDbContext WorkOrders(params IInterceptor[] interceptors) => new(Options<WorkOrdersDbContext>(WorkOrdersDbContext.Schema, interceptors));

    public ReportingDbContext Reporting(params IInterceptor[] interceptors) => new(Options<ReportingDbContext>(ReportingDbContext.Schema, interceptors));

    /// <summary>Creates the database (the first MigrateAsync does) and applies every migration of the three schemas the experiments touch.</summary>
    public async Task MigrateAsync()
    {
        await using var assets = Assets();
        await assets.Database.MigrateAsync();
        await using var workOrders = WorkOrders();
        await workOrders.Database.MigrateAsync();
        await using var reporting = Reporting();
        await reporting.Database.MigrateAsync();
    }

    public async Task SeedAsync(SeedSet seed)
    {
        // Parents first: work orders and facts reference assets by value (no FK), but assets reference the seeded lines.
        await BulkInsertAsync("assets.Assets", SeedTables.Assets(seed.Assets));
        await BulkInsertAsync("workorders.WorkOrders", SeedTables.WorkOrders(seed.WorkOrders));
        await BulkInsertAsync("reporting.WorkOrderFacts", SeedTables.Facts(seed.Facts));

        // Bulk loads leave statistics stale; the optimizer's row estimates (and so the plans) must reflect the data.
        await ExecuteAsync("UPDATE STATISTICS assets.Assets WITH FULLSCAN; UPDATE STATISTICS workorders.WorkOrders WITH FULLSCAN; UPDATE STATISTICS reporting.WorkOrderFacts WITH FULLSCAN;");
    }

    public async Task<string> ServerVersionAsync() =>
        ((string)(await ScalarAsync("SELECT @@VERSION") ?? "unknown")).ReplaceLineEndings(" ").Replace('\t', ' ');

    public async Task<IReadOnlyList<(string Table, long Rows)>> RowCountsAsync()
    {
        var counts = new List<(string, long)>();
        foreach (var table in new[] { "assets.ProductionLines", "assets.Assets", "workorders.WorkOrders", "reporting.WorkOrderFacts" })
        {
            counts.Add((table, Convert.ToInt64(await ScalarAsync($"SELECT COUNT_BIG(*) FROM {table}"), System.Globalization.CultureInfo.InvariantCulture)));
        }

        return counts;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private async Task BulkInsertAsync(string destination, DataTable rows)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, null)
        {
            DestinationTableName = destination,
            BatchSize = 5_000,
            BulkCopyTimeout = 0,
        };

        // By name, not position: the DataTable lists only the columns the seed sets.
        foreach (DataColumn column in rows.Columns)
        {
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulk.WriteToServerAsync(rows);
    }
}
