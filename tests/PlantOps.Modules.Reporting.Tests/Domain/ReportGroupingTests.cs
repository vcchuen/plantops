using PlantOps.Modules.Reporting.Queries;
using PlantOps.SharedKernel;

namespace PlantOps.Modules.Reporting.Tests.Domain;

public class ReportGroupingTests
{
    [Theory]
    [InlineData("line", "line")]
    [InlineData("ASSET", "asset")]
    [InlineData(" Month ", "month")]
    public void Known_names_parse_case_insensitively(string raw, string expected) =>
        Assert.Equal(expected, ReportGrouping.Name(ReportGrouping.Parse(raw, MttrGroup.Line)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void An_absent_value_is_the_default(string? raw) =>
        Assert.Equal(MttrGroup.Line, ReportGrouping.Parse(raw, MttrGroup.Line));

    [Theory]
    [InlineData("priority")] // valid for SLA, not for MTTR
    [InlineData("bogus")]
    [InlineData("1")] // Enum.TryParse would accept a number
    [InlineData("line,asset")]
    public void An_unknown_value_is_rejected_and_the_message_lists_the_choices(string raw)
    {
        var ex = Assert.Throws<DomainException>(() => ReportGrouping.Parse(raw, MttrGroup.Line));

        Assert.Contains("line, asset, month", ex.Message);
    }

    [Fact]
    public void Each_report_accepts_only_its_own_groupings()
    {
        Assert.Equal(SlaGroup.Priority, ReportGrouping.Parse("priority", SlaGroup.Month));
        Assert.Throws<DomainException>(() => ReportGrouping.Parse("line", SlaGroup.Priority));
        Assert.Equal(DowntimeGroup.Month, ReportGrouping.Parse("month", DowntimeGroup.Line));
        Assert.Throws<DomainException>(() => ReportGrouping.Parse("asset", DowntimeGroup.Line));
    }

    [Fact]
    public void The_response_echoes_the_group_name_in_lower_case() =>
        Assert.Equal("month", ReportGrouping.Name(MttrGroup.Month));

    [Fact]
    public void Keys_and_labels_follow_the_api_contract()
    {
        Assert.Equal(("2026-07", "Jul 2026"), ReportQueries.Month(new DateOnly(2026, 7, 1)));
        Assert.Equal(("P1", "P1 Critical"), ReportQueries.Priority("P1"));
        Assert.Equal(("Unknown", "Unknown"), ReportQueries.Priority("Unknown"));
        var line = Guid.NewGuid();
        Assert.Equal((line.ToString(), "SMT Line 1"), ReportQueries.Line(line, "SMT Line 1"));
        Assert.Equal(("unknown", "Unknown line"), ReportQueries.Line(null, "Unknown line"));
    }

    [Theory]
    [InlineData(75.04, 75.0)]
    [InlineData(75.06, 75.1)]
    [InlineData(66.666, 66.7)]
    [InlineData(0, 0)]
    public void Averages_and_percentages_are_rounded_to_one_decimal(double value, double expected) =>
        Assert.Equal(expected, ReportQueries.RoundOneDecimal(value));
}
