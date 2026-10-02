using PlantOps.SharedKernel;

namespace PlantOps.Modules.Assets.Domain;

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

    // Blank collapses to null so "" and "  " never end up stored as a serial number.
    public static string? Optional(string? value, string field, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, field, maxLength);
}
