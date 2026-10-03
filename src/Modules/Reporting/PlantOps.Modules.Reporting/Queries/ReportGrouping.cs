using PlantOps.SharedKernel;

namespace PlantOps.Modules.Reporting.Queries;

internal enum MttrGroup
{
    Line,
    Asset,
    Month,
}

internal enum SlaGroup
{
    Priority,
    Month,
}

internal enum DowntimeGroup
{
    Line,
    Month,
}

internal static class ReportGrouping
{
    /// <summary>Parses a groupBy value (case-insensitive); an absent value is the default, anything else is a 400.</summary>
    public static TGroup Parse<TGroup>(string? raw, TGroup fallback)
        where TGroup : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var text = raw.Trim();
        // Enum.TryParse also accepts numbers ("1"); only the names are part of the API.
        if (text.All(char.IsLetter) && Enum.TryParse<TGroup>(text, ignoreCase: true, out var group) && Enum.IsDefined(group))
        {
            return group;
        }

        var allowed = string.Join(", ", Enum.GetNames<TGroup>().Select(n => n.ToLowerInvariant()));
        throw new DomainException($"Unknown groupBy '{raw}'. Use one of: {allowed}.");
    }

    public static string Name<TGroup>(TGroup group)
        where TGroup : struct, Enum => group.ToString().ToLowerInvariant();
}
