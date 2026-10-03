using PlantOps.Modules.Reporting.Export;

namespace PlantOps.Modules.Reporting.Tests.Domain;

public class SpreadsheetTextTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"http://evil.example\",\"click\")")]
    [InlineData("=1+1")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1:A9)")]
    [InlineData("\t=1+1")]
    [InlineData("\r=1+1")]
    public void Text_that_could_be_a_formula_gets_a_leading_apostrophe(string input) =>
        Assert.Equal("'" + input, SpreadsheetText.Safe(input));

    [Theory]
    [InlineData("SMT Line 1")]
    [InlineData("Final Assembly 1")]
    [InlineData("a=b")] // only the first character decides
    [InlineData("1+1")]
    [InlineData("'=already quoted")]
    [InlineData(" =leading space")] // Excel does not treat this as a formula
    public void Harmless_text_is_left_alone(string input) =>
        Assert.Equal(input, SpreadsheetText.Safe(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_text_becomes_an_empty_string(string? input) =>
        Assert.Equal(string.Empty, SpreadsheetText.Safe(input));
}
