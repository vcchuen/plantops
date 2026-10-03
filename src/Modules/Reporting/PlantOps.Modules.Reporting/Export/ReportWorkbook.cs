using ClosedXML.Excel;
using PlantOps.Modules.Reporting.Queries;

namespace PlantOps.Modules.Reporting.Export;

/// <summary>Builds the Excel export: one sheet per report, a bold frozen header row, numbers as numbers.</summary>
internal static class ReportWorkbook
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string MttrByLineSheet = "MTTR by line";
    public const string SlaByPrioritySheet = "SLA by priority";
    public const string DowntimeByLineSheet = "Downtime by line";
    public const string MttrByMonthSheet = "MTTR by month";

    public static string FileName(DateOnly from, DateOnly to) => $"plantops-reports-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.xlsx";

    public static XLWorkbook Build(
        IReadOnlyList<MttrRow> mttrByLine,
        IReadOnlyList<SlaRow> slaByPriority,
        IReadOnlyList<DowntimeRow> downtimeByLine,
        IReadOnlyList<MttrRow> mttrByMonth)
    {
        var workbook = new XLWorkbook();

        Sheet(workbook, MttrByLineSheet, ["Line", "Work orders", "Mean repair (min)"], mttrByLine,
            r => [r.Label, r.WorkOrders, r.MeanRepairMinutes]);
        Sheet(workbook, SlaByPrioritySheet, ["Priority", "Completed", "Met SLA", "Compliance (%)"], slaByPriority,
            r => [r.Label, r.Completed, r.MetSla, r.CompliancePercent]);
        Sheet(workbook, DowntimeByLineSheet, ["Line", "Events", "Downtime (min)"], downtimeByLine,
            r => [r.Label, r.Events, r.DowntimeMinutes]);
        Sheet(workbook, MttrByMonthSheet, ["Month", "Work orders", "Mean repair (min)"], mttrByMonth,
            r => [r.Label, r.WorkOrders, r.MeanRepairMinutes]);

        return workbook;
    }

    // A cell is a string (escaped by SpreadsheetText) or a number; nothing else reaches a sheet.
    private static void Sheet<TRow>(
        XLWorkbook workbook,
        string name,
        string[] headers,
        IReadOnlyList<TRow> rows,
        Func<TRow, object[]> cells)
    {
        var sheet = workbook.Worksheets.Add(name);
        var widths = new int[headers.Length];

        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
            widths[c] = headers[c].Length;
        }

        var header = sheet.Range(1, 1, 1, headers.Length);
        header.Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);

        for (var r = 0; r < rows.Count; r++)
        {
            var values = cells(rows[r]);
            for (var c = 0; c < values.Length; c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                switch (values[c])
                {
                    case string text:
                        var safe = SpreadsheetText.Safe(text);
                        cell.Value = safe;
                        widths[c] = Math.Max(widths[c], safe.Length);
                        break;
                    case int number:
                        cell.Value = number;
                        break;
                    case double number:
                        cell.Value = number;
                        cell.Style.NumberFormat.Format = "0.0";
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported cell type {values[c].GetType().Name}.");
                }
            }
        }

        // Width in characters, measured from the text rather than by AdjustToContents: that needs font metrics, which a
        // slim Linux container may lack, and the sheets are tiny. Numbers fit the header's width.
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Column(c + 1).Width = Math.Clamp(widths[c] + 2, 10, 60);
        }
    }
}
