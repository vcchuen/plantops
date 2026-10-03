using ClosedXML.Excel;
using PlantOps.Modules.Reporting.Export;
using PlantOps.Modules.Reporting.Queries;

namespace PlantOps.Modules.Reporting.Tests.Domain;

public class ReportWorkbookTests
{
    private const string Formula = "=HYPERLINK(\"http://evil.example\",\"click\")";

    // Saved and re-opened, like the manager's Excel would: what matters is what is in the file, not in memory.
    private static XLWorkbook RoundTrip(
        IReadOnlyList<MttrRow>? mttrByLine = null,
        IReadOnlyList<SlaRow>? sla = null,
        IReadOnlyList<DowntimeRow>? downtime = null,
        IReadOnlyList<MttrRow>? mttrByMonth = null)
    {
        using var built = ReportWorkbook.Build(mttrByLine ?? [], sla ?? [], downtime ?? [], mttrByMonth ?? []);
        var stream = new MemoryStream();
        built.SaveAs(stream);
        stream.Position = 0;
        return new XLWorkbook(stream);
    }

    [Fact]
    public void The_workbook_has_one_sheet_per_report_in_order()
    {
        using var workbook = RoundTrip();

        Assert.Equal(
            ["MTTR by line", "SLA by priority", "Downtime by line", "MTTR by month"],
            workbook.Worksheets.Select(w => w.Name));
    }

    [Fact]
    public void Headers_are_bold_and_frozen_and_values_are_numbers()
    {
        using var workbook = RoundTrip(
            mttrByLine: [new MttrRow("l1", "SMT Line 1", 4, 75.5)],
            sla: [new SlaRow("P1", "P1 Critical", 4, 3, 75.0)],
            downtime: [new DowntimeRow("l1", "SMT Line 1", 2, 190)]);

        var mttr = workbook.Worksheet("MTTR by line");
        Assert.Equal("Line", mttr.Cell(1, 1).GetString());
        Assert.True(mttr.Cell(1, 1).Style.Font.Bold);
        Assert.Equal(1, mttr.SheetView.SplitRow);
        Assert.Equal("SMT Line 1", mttr.Cell(2, 1).GetString());
        Assert.Equal(XLDataType.Number, mttr.Cell(2, 2).DataType);
        Assert.Equal(4, mttr.Cell(2, 2).GetDouble());
        Assert.Equal(XLDataType.Number, mttr.Cell(2, 3).DataType);
        Assert.Equal(75.5, mttr.Cell(2, 3).GetDouble());

        var sheet = workbook.Worksheet("SLA by priority");
        Assert.Equal("P1 Critical", sheet.Cell(2, 1).GetString());
        Assert.Equal(3, sheet.Cell(2, 3).GetDouble());
        Assert.Equal(75.0, sheet.Cell(2, 4).GetDouble());

        var down = workbook.Worksheet("Downtime by line");
        Assert.Equal(190, down.Cell(2, 3).GetDouble());
    }

    [Fact]
    public void A_label_that_looks_like_a_formula_is_stored_as_escaped_text_not_a_formula()
    {
        using var workbook = RoundTrip(mttrByLine: [new MttrRow("l1", Formula, 1, 10)]);

        var cell = workbook.Worksheet("MTTR by line").Cell(2, 1);

        Assert.False(cell.HasFormula);
        Assert.Equal(XLDataType.Text, cell.DataType);
        // ClosedXML reads a leading apostrophe the way Excel does: it is the "quote prefix" marker, not part of the text.
        // The file holds the text itself, as a string cell flagged with the quote prefix style, so it shows as text
        // and stays text when the manager edits the cell.
        Assert.Equal(Formula, cell.GetString());
        Assert.True(cell.Style.IncludeQuotePrefix);
    }

    [Fact]
    public void Column_widths_fit_the_longest_text()
    {
        using var workbook = RoundTrip(mttrByLine: [new MttrRow("l1", "A very long production line name indeed", 1, 1)]);

        var sheet = workbook.Worksheet("MTTR by line");

        Assert.True(sheet.Column(1).Width > sheet.Column(2).Width);
    }

    [Fact]
    public void The_file_name_carries_the_range()
    {
        Assert.Equal(
            "plantops-reports-2026-07-06-2026-10-04.xlsx",
            ReportWorkbook.FileName(new DateOnly(2026, 7, 6), new DateOnly(2026, 10, 4)));
    }
}
