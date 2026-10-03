using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using PlantOps.Api.Hosting;

namespace PlantOps.Api.Tests;

public class AzureHostingTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void Nothing_configured_enables_nothing()
    {
        var options = AzureHostingOptions.From(Config());

        Assert.Null(options.KeyVaultUri);
        Assert.False(options.DataProtectionEnabled);
        Assert.False(options.MonitorEnabled);
    }

    [Fact]
    public void Blank_values_count_as_not_configured()
    {
        var options = AzureHostingOptions.From(Config(
            ("KeyVault:Uri", ""),
            ("DataProtection:BlobUri", " "),
            ("DataProtection:KeyId", ""),
            ("APPLICATIONINSIGHTS_CONNECTION_STRING", "")));

        Assert.Null(options.KeyVaultUri);
        Assert.False(options.DataProtectionEnabled);
        Assert.False(options.MonitorEnabled);
    }

    [Fact]
    public void Data_protection_needs_both_the_blob_and_the_key()
    {
        var blobOnly = AzureHostingOptions.From(Config(("DataProtection:BlobUri", "https://acct.blob.core.windows.net/keys/keys.xml")));
        var both = AzureHostingOptions.From(Config(
            ("DataProtection:BlobUri", "https://acct.blob.core.windows.net/keys/keys.xml"),
            ("DataProtection:KeyId", "https://vault.vault.azure.net/keys/dp/abc")));

        Assert.False(blobOnly.DataProtectionEnabled);
        Assert.True(both.DataProtectionEnabled);
    }

    [Fact]
    public void Each_key_turns_on_its_own_feature()
    {
        var options = AzureHostingOptions.From(Config(
            ("KeyVault:Uri", "https://vault.vault.azure.net/"),
            ("APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=00000000-0000-0000-0000-000000000000")));

        Assert.Equal(new Uri("https://vault.vault.azure.net/"), options.KeyVaultUri);
        Assert.True(options.MonitorEnabled);
        Assert.False(options.DataProtectionEnabled);
    }

    [Fact]
    public void Malformed_uri_fails_loudly_instead_of_falling_back_to_local_keys()
    {
        Assert.Throws<UriFormatException>(() => AzureHostingOptions.From(Config(("KeyVault:Uri", "not a uri"))));
    }

    [Fact]
    public void Host_starts_with_defaults_and_registers_no_azure_services()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:PlantOps", string.Empty));

        // Forces the host to build and start.
        using var client = factory.CreateClient();

        // Default data protection: no Azure blob repository, no Key Vault key wrapping.
        var keyOptions = factory.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        Assert.Null(keyOptions.XmlRepository);
        Assert.Null(keyOptions.XmlEncryptor);
        Assert.NotNull(factory.Services.GetService<IDataProtectionProvider>());

        // No Azure Monitor / OpenTelemetry pipeline without the connection string.
        Assert.Null(factory.Services.GetService<TracerProvider>());
    }
}
