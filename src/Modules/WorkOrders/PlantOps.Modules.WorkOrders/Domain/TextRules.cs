using PlantOps.SharedKernel;

namespace PlantOps.Modules.WorkOrders.Domain;

internal static class TextRules
{
    public static string Required(string? value, string field, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainException($"{field} is required.");
        }

        return trimmed.Length > maxLength
            ? throw new DomainException($"{field} must be at most {maxLength} characters.")
            : trimmed;
    }

    // Blank collapses to empty so callers never store whitespace.
    public static string Optional(string? value, string field, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : Required(value, field, maxLength);
}
