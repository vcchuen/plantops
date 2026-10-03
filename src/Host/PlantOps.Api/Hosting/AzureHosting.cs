using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.DataProtection;

// Not "PlantOps.Api.Azure": that namespace would hide the real Azure.* namespaces for the rest of the project.
namespace PlantOps.Api.Hosting;

/// <summary>
/// What the Azure deployment asks of the host (design 09). Every part is opt-in by configuration: with none of the
/// keys below set, nothing here registers anything and the app behaves exactly as it does locally and in compose.
/// </summary>
internal sealed record AzureHostingOptions(
    Uri? KeyVaultUri,
    Uri? DataProtectionBlobUri,
    Uri? DataProtectionKeyId,
    bool MonitorEnabled)
{
    // Both halves are needed: blob-only would store keys in plain text, key-only has nowhere to store them.
    public bool DataProtectionEnabled => DataProtectionBlobUri is not null && DataProtectionKeyId is not null;

    /// <summary>Pure decision from configuration, so it is unit-testable without any Azure call.</summary>
    public static AzureHostingOptions From(IConfiguration configuration) => new(
        ParseUri(configuration["KeyVault:Uri"]),
        ParseUri(configuration["DataProtection:BlobUri"]),
        ParseUri(configuration["DataProtection:KeyId"]),
        // The Azure Monitor distro itself reads this variable; we only decide whether to turn it on.
        !string.IsNullOrWhiteSpace(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]));

    // Blank means "not configured" (compose and tests pass empty values); a non-blank malformed value is a deployment
    // mistake that must fail at startup, not silently fall back to local keys.
    private static Uri? ParseUri(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new Uri(value, UriKind.Absolute);
}

internal static class AzureHosting
{
    public static WebApplicationBuilder AddAzureHosting(this WebApplicationBuilder builder)
    {
        var options = AzureHostingOptions.From(builder.Configuration);

        // One credential for every Azure client: it caches tokens, so Key Vault, Blob and Key Vault keys share them
        // instead of each asking the managed-identity endpoint on its own. Created lazily so nothing runs when unused.
        var credential = new Lazy<TokenCredential>(() => new DefaultAzureCredential());

        if (options.KeyVaultUri is { } vault)
        {
            // Secret names cannot contain ':', so a secret named "ConnectionStrings--PlantOps" arrives as the key
            // "ConnectionStrings:PlantOps". Added last, so Key Vault overrides appsettings and environment variables.
            builder.Configuration.AddAzureKeyVault(vault, credential.Value);
        }

        if (options.DataProtectionEnabled)
        {
            // Without this the key ring lives in the container's file system and every restart or scale-out instance
            // invalidates the __Host- session cookies (the M3 gap). Keys sit in Blob Storage, wrapped by a Key Vault key.
            builder.Services.AddDataProtection()
                .SetApplicationName("PlantOps")
                .PersistKeysToAzureBlobStorage(options.DataProtectionBlobUri!, credential.Value)
                .ProtectKeysWithAzureKeyVault(options.DataProtectionKeyId!, credential.Value);
        }
        // else: ASP.NET Core defaults. Local runs and containers lose keys on restart, which is fine for dev.

        if (options.MonitorEnabled)
        {
            // The distro wires traces, metrics and logs (including the "PlantOps.Security" category, subject to the
            // normal Logging:LogLevel filters) to Application Insights. The app defines no custom Meter or
            // ActivitySource yet, so there is nothing extra to subscribe to.
            builder.Services.AddOpenTelemetry().UseAzureMonitor();
        }

        return builder;
    }
}
