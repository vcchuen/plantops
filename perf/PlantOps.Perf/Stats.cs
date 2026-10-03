using System.Numerics;
using System.Text;

namespace PlantOps.Perf;

internal static class Stats
{
    /// <summary>The middle value; the mean of the two middle values for an even count. Throws on an empty sequence.</summary>
    public static double Median<T>(IEnumerable<T> values)
        where T : INumber<T>
    {
        ArgumentNullException.ThrowIfNull(values);
        var sorted = values.Select(double.CreateChecked).Order().ToArray();
        if (sorted.Length == 0)
        {
            throw new InvalidOperationException("The median of nothing is undefined.");
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}

internal static class MarkdownTable
{
    /// <summary>A GitHub-flavoured table. Pipes and line breaks inside cells are escaped so a cell can never break the layout.</summary>
    public static string Render(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        builder.Append("| ").AppendJoin(" | ", headers.Select(Cell)).AppendLine(" |");
        builder.Append("| ").AppendJoin(" | ", headers.Select(_ => "---")).AppendLine(" |");
        foreach (var row in rows)
        {
            if (row.Count != headers.Count)
            {
                throw new ArgumentException($"A row has {row.Count} cells but the table has {headers.Count} columns.", nameof(rows));
            }

            builder.Append("| ").AppendJoin(" | ", row.Select(Cell)).AppendLine(" |");
        }

        return builder.ToString();
    }

    private static string Cell(string value) => value
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("\r", string.Empty, StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);
}
