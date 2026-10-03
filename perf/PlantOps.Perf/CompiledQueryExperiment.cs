using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Microsoft.EntityFrameworkCore;
using PlantOps.Modules.WorkOrders.Domain;
using PlantOps.Modules.WorkOrders.Infrastructure;

namespace PlantOps.Perf;

/// <summary>
/// Experiment 3: EF.CompileAsyncQuery versus the regular LINQ for the work order detail read (by id, as the GET endpoint does:
/// AsNoTracking + FirstOrDefault on the key). Each call uses a fresh DbContext, like one HTTP request. The options (hence
/// EF's model and service provider) are built once in setup, as the application does.
/// BenchmarkDotNet needs a public class and public methods; the internal EF types stay in private members.
/// </summary>
[MemoryDiagnoser]
public class WorkOrderDetailBenchmarks
{
    private DbContextOptions<WorkOrdersDbContext> _options = null!;
    private WorkOrderId _id;

    [GlobalSetup]
    public void Setup()
    {
        var connectionString = Environment.GetEnvironmentVariable(PerfEnvironment.ConnectionString)
            ?? throw new InvalidOperationException($"{PerfEnvironment.ConnectionString} is not set; Program sets it before starting BenchmarkDotNet.");

        _options = new DbContextOptionsBuilder<WorkOrdersDbContext>().UseSqlServer(connectionString).Options;

        // A row from the middle of the table (by number), so the id is neither the first nor the last page of the index.
        using var db = new WorkOrdersDbContext(_options);
        _id = db.WorkOrders.AsNoTracking().OrderBy(w => w.Number).Skip(10_000).Select(w => w.Id).First();
    }

    [Benchmark(Baseline = true)]
    public async Task<int> RegularLinq()
    {
        await using var db = new WorkOrdersDbContext(_options);
        var id = _id;
        var workOrder = await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);
        return workOrder!.Number;
    }

    [Benchmark]
    public async Task<int> CompiledQuery()
    {
        await using var db = new WorkOrdersDbContext(_options);
        var workOrder = await DetailQueries.CompiledById(db, _id);
        return workOrder!.Number;
    }
}

internal static class DetailQueries
{
    // The compiled delegate caches the translated query (expression tree to SQL). The regular path pays the LINQ
    // translation cache lookup (hashing the expression) on every call instead; the question is whether that is measurable.
    public static readonly Func<WorkOrdersDbContext, WorkOrderId, Task<WorkOrder?>> CompiledById =
        EF.CompileAsyncQuery((WorkOrdersDbContext db, WorkOrderId id) =>
            db.WorkOrders.AsNoTracking().FirstOrDefault(w => w.Id == id));
}

internal static class PerfEnvironment
{
    /// <summary>Program sets it; the benchmark class reads it (BenchmarkDotNet constructs benchmark classes itself, so it cannot be passed in).</summary>
    public const string ConnectionString = "PLANTOPS_PERF_CONNECTION";
}

internal static class CompiledQueryExperiment
{
    /// <summary>
    /// Runs BenchmarkDotNet in this process (InProcessEmitToolchain). The default toolchain generates a separate project,
    /// restores and builds it, and starts a child process per benchmark: that needs the SDK and the NuGet feed at run
    /// time, has to find this project's references and internals, and adds minutes. In process, the benchmark runs
    /// against the already-built assembly. The cost: no process isolation, which a ShortRun database round-trip
    /// benchmark does not need.
    /// </summary>
    public static string Run(string connectionString)
    {
        Environment.SetEnvironmentVariable(PerfEnvironment.ConnectionString, connectionString);

        var config = DefaultConfig.Instance
            .AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance))
            .WithOptions(ConfigOptions.DisableOptimizationsValidator);

        var summary = BenchmarkRunner.Run<WorkOrderDetailBenchmarks>(config);

        var text = new System.Text.StringBuilder();
        text.AppendLine("## Experiment 3: compiled query versus regular LINQ");
        text.AppendLine();
        text.AppendLine("Work order detail by id (AsNoTracking, FirstOrDefault on the key), BenchmarkDotNet ShortRun, in-process toolchain, fresh DbContext per call, SQL Server in a container on the same machine.");
        text.AppendLine("Rule from design 07: keep the compiled query only if the difference is larger than the error bars; otherwise the regular query stays.");
        text.AppendLine();

        if (summary.HasCriticalValidationErrors)
        {
            text.AppendLine("BenchmarkDotNet reported critical validation errors, so there is no table:");
            text.AppendLine();
            foreach (var error in summary.ValidationErrors)
            {
                text.AppendLine($"- {error.Message}");
            }
        }
        else
        {
            var report = Directory.Exists(summary.ResultsDirectoryPath)
                ? Directory.GetFiles(summary.ResultsDirectoryPath, "*WorkOrderDetailBenchmarks-report-github.md").FirstOrDefault()
                : null;
            text.AppendLine(report is null ? "BenchmarkDotNet produced no GitHub-markdown report." : File.ReadAllText(report));
        }

        text.AppendLine();
        return text.ToString();
    }
}
