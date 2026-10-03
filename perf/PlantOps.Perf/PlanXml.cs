using System.Xml.Linq;

namespace PlantOps.Perf;

/// <summary>One physical operator of a showplan. <see cref="Table"/>/<see cref="Index"/> are set for data-access operators (scan, seek, lookup).</summary>
internal sealed record PlanOperator(string PhysicalOp, string? Table, string? Index, IReadOnlyList<string> Columns)
{
    public string Label => Index is null ? PhysicalOp : $"{PhysicalOp} [{Index}]";
}

internal sealed record PlanSummary(IReadOnlyList<PlanOperator> Operators)
{
    /// <summary>Operators from the data source upwards ("Clustered Index Scan [PK] > Hash Match > ..."), repeats collapsed.</summary>
    public string Describe() => Operators.Count == 0
        ? "(no operators)"
        : string.Join(" > ", Operators.Reverse().Select(o => o.Label).Distinct());

    /// <summary>
    /// Every column of <paramref name="table"/> the plan's data-access operators output or filter on: what an index
    /// would have to contain to cover the query.
    /// </summary>
    public IReadOnlyList<string> NeededColumns(string table) => [.. Operators
        .Where(o => string.Equals(o.Table, table, StringComparison.OrdinalIgnoreCase))
        .SelectMany(o => o.Columns)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)];
}

/// <summary>Reads the XML SQL Server returns for SET STATISTICS XML ON. Matches on local names, so the showplan namespace/version does not matter.</summary>
internal static class PlanXml
{
    public static PlanSummary Parse(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        var document = XDocument.Parse(xml);

        var operators = new List<PlanOperator>();
        foreach (var relOp in document.Descendants().Where(e => e.Name.LocalName == "RelOp"))
        {
            var physical = (string?)relOp.Attribute("PhysicalOp") ?? "?";

            // <RelOp><IndexScan><Object Table= Index= /></IndexScan></RelOp>: the Object is a grandchild of the RelOp.
            // Operators that only combine children (Hash Match, Sort, ...) have none, so they are not data access.
            var obj = relOp.Elements().SelectMany(e => e.Elements()).FirstOrDefault(e => e.Name.LocalName == "Object");
            if (obj is null)
            {
                operators.Add(new PlanOperator(physical, null, null, []));
                continue;
            }

            var table = Unbracket((string?)obj.Attribute("Table"));
            var index = Unbracket((string?)obj.Attribute("Index"));

            // Access operators are leaves, so every ColumnReference below one belongs to it (output list, seek and
            // residual predicates). References to other things (Expr1002, other tables) are dropped.
            var columns = relOp.Descendants()
                .Where(e => e.Name.LocalName == "ColumnReference")
                .Where(e => string.Equals(Unbracket((string?)e.Attribute("Table")), table, StringComparison.OrdinalIgnoreCase))
                .Select(e => Unbracket((string?)e.Attribute("Column")))
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            operators.Add(new PlanOperator(physical, table, index, columns));
        }

        return new PlanSummary(operators);
    }

    private static string? Unbracket(string? name) => name?.Trim('[', ']');
}
