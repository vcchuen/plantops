using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace PlantOps.Perf;

/// <summary>
/// Starts SQL Server in a container, migrates and seeds it, runs the three experiments and writes perf-results.md.
/// Run with: dotnet run -c Release --project perf/PlantOps.Perf -- perf-results.md
/// (Release matters for Experiment 3; Experiments 1 and 2 measure SQL Server and are unaffected.)
/// An explicit entry class, not top-level statements, so no global "Program" type exists to clash with another host's.
/// </summary>
internal static class PerfProgram
{
    private const string Image = "mcr.microsoft.com/mssql/server:2022-latest";

    private static async Task<int> Main(string[] args)
    {
        var outputPath = args.Length > 0 ? args[0] : "perf-results.md";
        var failures = 0;

#if DEBUG
        Console.WriteLine("WARNING: Debug build. Experiment 3 numbers are meaningless; use -c Release.");
#endif

        // The container is disposed on every path (ryuk would reap it eventually, but not before the runner is reused).
        await using var container = new MsSqlBuilder(Image).Build();
        Console.WriteLine($"Starting {Image} ...");
        await container.StartAsync();

        var connectionString = new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = "PlantOpsPerf",
        }.ConnectionString;
        var database = new PerfDatabase(connectionString);

        var seedOptions = new SeedOptions();
        var watch = Stopwatch.StartNew();
        await database.MigrateAsync();
        var migrateTime = watch.Elapsed;

        watch.Restart();
        var seed = SeedGenerator.Generate(seedOptions);
        var generateTime = watch.Elapsed;

        watch.Restart();
        await database.SeedAsync(seed);
        var insertTime = watch.Elapsed;
        Console.WriteLine($"Migrated in {migrateTime.TotalSeconds:0.0}s, generated in {generateTime.TotalSeconds:0.0}s, bulk-inserted in {insertTime.TotalSeconds:0.0}s.");

        var report = new StringBuilder();
        report.Append(await HeaderAsync(database, seedOptions, migrateTime, generateTime, insertTime));

        failures += await RunAsync("Experiment 1: index selection", report, () => IndexExperiment.RunAsync(database));
        failures += await RunAsync("Experiment 2: N+1 versus projection", report, () => NPlusOneExperiment.RunAsync(database));
        failures += await RunAsync("Experiment 3: compiled query", report, () => Task.FromResult(CompiledQueryExperiment.Run(connectionString)));

        await File.WriteAllTextAsync(outputPath, report.ToString());
        Console.WriteLine($"Wrote {Path.GetFullPath(outputPath)}");
        return failures == 0 ? 0 : 1;
    }

    // One failed experiment must not lose the others' numbers (the first real run is in CI, where retries are slow):
    // its section says what went wrong and the exit code turns red.
    private static async Task<int> RunAsync(string name, StringBuilder report, Func<Task<string>> experiment)
    {
        Console.WriteLine($"== {name}");
        try
        {
            report.Append(await experiment());
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            report.AppendLine($"## {name}");
            report.AppendLine();
            report.AppendLine("**FAILED**:");
            report.AppendLine();
            report.AppendLine("```");
            report.AppendLine(exception.ToString());
            report.AppendLine("```");
            report.AppendLine();
            return 1;
        }
    }

    private static async Task<string> HeaderAsync(PerfDatabase database, SeedOptions options, TimeSpan migrate, TimeSpan generate, TimeSpan insert)
    {
        var text = new StringBuilder();
        text.AppendLine("# PlantOps performance results");
        text.AppendLine();
        text.AppendLine("> Shared CI runners are noisy and the SQL Server container shares the machine with the harness. Compare ratios within this run, never absolute numbers across runs.");
        text.AppendLine();

        var sha = Environment.GetEnvironmentVariable("GITHUB_SHA");
        var runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
        var counts = await database.RowCountsAsync();
        text.AppendLine(MarkdownTable.Render(
            ["item", "value"],
            new (string, string)[]
            {
                ("date (UTC)", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                ("git SHA", string.IsNullOrEmpty(sha) ? "(not in CI)" : sha),
                ("workflow run id", string.IsNullOrEmpty(runId) ? "(not in CI)" : runId),
                ("runner", $"{Environment.ProcessorCount} logical CPUs, {RuntimeInformation.OSDescription}, {RuntimeInformation.FrameworkDescription}"),
                ("SQL Server", await database.ServerVersionAsync()),
                ("container image", Image),
                ("seed", $"Random seed {options.Seed}, {options.Months} months ending {SeedOptions.End:yyyy-MM-dd}"),
                ("row counts", string.Join("; ", counts.Select(c => $"{c.Table} = {c.Rows:N0}"))),
                ("setup time", $"migrations {migrate.TotalSeconds:0.0}s, generating {generate.TotalSeconds:0.0}s, bulk insert {insert.TotalSeconds:0.0}s"),
            }.Select(r => (IReadOnlyList<string>)[r.Item1, r.Item2])));
        return text.ToString();
    }
}
