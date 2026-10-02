using System.Text.RegularExpressions;
using PlantOps.SharedKernel;

namespace PlantOps.Modules.Assets.Domain;

internal sealed partial record AssetTag
{
    public const int MinLength = 3;
    public const int MaxLength = 20;

    private AssetTag(string value) => Value = value;

    public string Value { get; }

    public static AssetTag Create(string? value)
    {
        var normalised = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalised))
        {
            throw new DomainException("Asset tag is required.");
        }

        if (normalised.Length is < MinLength or > MaxLength)
        {
            throw new DomainException($"Asset tag must be {MinLength}-{MaxLength} characters long.");
        }

        return Pattern().IsMatch(normalised)
            ? new AssetTag(normalised)
            : throw new DomainException(
                "Asset tag may only contain letters, digits and single hyphens between groups (e.g. SMT1-PNP-01).");
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Z0-9]+(-[A-Z0-9]+)*$")]
    private static partial Regex Pattern();
}
