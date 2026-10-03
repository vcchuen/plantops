using System.Globalization;
using System.Text.RegularExpressions;

namespace PlantOps.Perf;

internal sealed record SqlTiming(int CpuMs, int ElapsedMs);

/// <summary>Parses the text SQL Server sends for SET STATISTICS IO / TIME (delivered as SqlConnection.InfoMessage).</summary>
internal static partial class StatisticsMessages
{
    /// <summary>
    /// Sum of "logical reads" over every table line, e.g. "Table 'X'. Scan count 1, logical reads 147, physical reads 0, ...".
    /// "lob logical reads" is a different counter and is not included.
    /// </summary>
    public static long LogicalReads(IEnumerable<string> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        long total = 0;
        foreach (var message in messages)
        {
            foreach (Match match in LogicalReadsPattern().Matches(message))
            {
                total += long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        return total;
    }

    /// <summary>
    /// The last "SQL Server Execution Times" block. The "parse and compile time" block has the same CPU/elapsed
    /// shape but measures compilation, so it is ignored. Null when no execution block is present.
    /// </summary>
    public static SqlTiming? ExecutionTime(IEnumerable<string> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        SqlTiming? last = null;
        foreach (var message in messages)
        {
            if (!message.Contains("Execution Times", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = TimePattern().Match(message);
            if (match.Success)
            {
                last = new SqlTiming(
                    int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
            }
        }

        return last;
    }

    [GeneratedRegex(@"(?<!lob )logical reads (\d+)")]
    private static partial Regex LogicalReadsPattern();

    [GeneratedRegex(@"CPU time = (\d+) ms,\s+elapsed time = (\d+) ms")]
    private static partial Regex TimePattern();
}
