using System.Data;
using PlantOps.Modules.Reporting.Domain;

namespace PlantOps.Perf;

/// <summary>
/// Turns the seed records into DataTables for SqlBulkCopy. Columns are matched by NAME (see <see cref="SeedDatabase"/>),
/// so they are listed here only for the columns the table requires or the experiments read; nullable ones are left
/// out when the seed never sets them. Date-only columns use DateTime (the type SqlBulkCopy maps to SQL "date").
/// </summary>
internal static class SeedTables
{
    public static DataTable Assets(IEnumerable<AssetSeed> assets)
    {
        var table = new DataTable();
        Add<Guid>(table, "Id");
        Add<string>(table, "Tag");
        Add<string>(table, "Name");
        Add<string>(table, "Manufacturer");
        Add<string>(table, "Model");
        Add<Guid>(table, "LineId");
        Add<string>(table, "Station");
        Add<string>(table, "Criticality");
        Add<string>(table, "Status");
        Add<DateTime>(table, "CommissionedOn");

        foreach (var a in assets)
        {
            table.Rows.Add(a.Id, a.Tag, a.Name, a.Manufacturer, a.Model, a.Line.Id, a.Station, a.Criticality, "InService", a.CommissionedOn);
        }

        return table;
    }

    public static DataTable WorkOrders(IEnumerable<WorkOrderSeed> workOrders)
    {
        var table = new DataTable();
        Add<Guid>(table, "Id");
        Add<int>(table, "Number");
        Add<Guid>(table, "AssetId");
        Add<string>(table, "AssetTag");
        Add<string>(table, "AssetName");
        Add<string>(table, "Title");
        Add<string>(table, "Description");
        Add<string>(table, "Priority");
        Add<string>(table, "Status");
        Add<bool>(table, "AssetDown");
        Add<string>(table, "Source");
        Add<string>(table, "ReportedById");
        Add<string>(table, "ReportedByName");
        Add<DateTimeOffset>(table, "SubmittedAt");
        Add<DateTimeOffset>(table, "DueAt");
        Add<DateTimeOffset>(table, "ApprovedAt");
        Add<string>(table, "AssignedToId");
        Add<string>(table, "AssignedToName");
        Add<DateTimeOffset>(table, "StartedAt");
        Add<DateTimeOffset>(table, "CompletedAt");
        Add<DateTimeOffset>(table, "ClosedAt");

        foreach (var w in workOrders)
        {
            table.Rows.Add(
                w.Id,
                w.Number,
                w.Asset.Id,
                w.Asset.Tag,
                w.Asset.Name,
                w.Title,
                "Seeded work order for performance measurement.",
                w.Priority,
                w.Status,
                w.AssetDown,
                w.Source,
                w.ReportedById,
                w.ReportedById,
                w.SubmittedAt,
                w.DueAt,
                Db(w.ApprovedAt),
                Db(w.AssignedToId),
                Db(w.AssignedToId),
                Db(w.StartedAt),
                Db(w.CompletedAt),
                Db(w.ClosedAt));
        }

        return table;
    }

    public static DataTable Facts(IEnumerable<WorkOrderFact> facts)
    {
        var table = new DataTable();
        Add<Guid>(table, "WorkOrderId");
        Add<string>(table, "Number");
        Add<Guid>(table, "AssetId");
        Add<string>(table, "AssetTag");
        Add<Guid>(table, "LineId");
        Add<string>(table, "LineName");
        Add<string>(table, "Priority");
        Add<string>(table, "Source");
        Add<DateTimeOffset>(table, "SubmittedAt");
        Add<DateTimeOffset>(table, "StartedAt");
        Add<DateTimeOffset>(table, "CompletedAt");
        Add<DateTimeOffset>(table, "DueAt");
        Add<bool>(table, "MetSla");
        Add<int>(table, "RepairMinutes");
        Add<int>(table, "DowntimeMinutes");
        Add<DateTime>(table, "CompletedMonth");

        foreach (var f in facts)
        {
            table.Rows.Add(
                f.WorkOrderId,
                f.Number,
                f.AssetId,
                f.AssetTag,
                Db(f.LineId),
                f.LineName,
                f.Priority,
                f.Source,
                f.SubmittedAt,
                f.StartedAt,
                f.CompletedAt,
                f.DueAt,
                f.MetSla,
                f.RepairMinutes,
                Db(f.DowntimeMinutes),
                f.CompletedMonth.ToDateTime(TimeOnly.MinValue));
        }

        return table;
    }

    private static void Add<T>(DataTable table, string name)
    {
        var column = table.Columns.Add(name, typeof(T));
        column.AllowDBNull = true;
    }

    private static object Db<T>(T? value)
        where T : struct => value.HasValue ? value.Value : DBNull.Value;

    private static object Db(string? value) => value is null ? DBNull.Value : value;
}
