namespace PlantOps.Modules.Identity;

// Bound from the "Auth" configuration section (design 03, Decision 6).
internal sealed class AuthOptions
{
    public const string Section = "Auth";

    public string? Authority { get; set; }

    // Set when the API reaches the IdP by a different host name than the browser does (docker compose).
    public string? MetadataAddress { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Authority) || !string.IsNullOrWhiteSpace(MetadataAddress);
}
