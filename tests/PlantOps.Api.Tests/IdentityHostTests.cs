using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PlantOps.Modules.Identity;
using PlantOps.Modules.Identity.Contracts;

namespace PlantOps.Api.Tests;

// Offline: no IdP, no database. Authorization and the CSRF guard run before any handler touches the DB.
public class IdentityHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WebApplicationFactory<Program> CreateFactory(Action<IWebHostBuilder>? configure = null, string? webRoot = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PlantOps", string.Empty);
            if (webRoot is not null)
            {
                builder.UseSetting(WebHostDefaults.WebRootKey, webRoot);
            }

            configure?.Invoke(builder);
        });

    private static WebApplicationFactory<Program> CreateTestAuthFactory() =>
        CreateFactory(b => b.ConfigureTestServices(s => s.AddTestAuthentication()));

    // Static OIDC metadata so the redirect to /authorize can be asserted without a network.
    private static WebApplicationFactory<Program> CreateOidcFactory() =>
        CreateFactory(b =>
        {
            b.UseSetting("Auth:Authority", "https://idp.test");
            b.UseSetting("Auth:ClientId", "plantops-web");
            b.UseSetting("Auth:ClientSecret", "secret");
            b.ConfigureTestServices(s => s.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, o =>
            {
                var configuration = new OpenIdConnectConfiguration
                {
                    AuthorizationEndpoint = "https://idp.test/authorize",
                    EndSessionEndpoint = "https://idp.test/logout",
                    Issuer = "https://idp.test",
                };
                // The built-in post-configure already created a manager from the authority; replace it,
                // setting Configuration alone would not be used.
                o.Configuration = configuration;
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            }));
        });

    private static HttpClient NoRedirectClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Anonymous_api_request_gets_401_problem_not_a_redirect()
    {
        await using var factory = CreateFactory();
        using var client = NoRedirectClient(factory);

        var response = await client.GetAsync("/api/assets", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Anonymous_api_request_gets_401_even_with_the_real_oidc_scheme_configured()
    {
        await using var factory = CreateOidcFactory();
        using var client = NoRedirectClient(factory);

        var response = await client.GetAsync("/api/assets", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Health_live_stays_anonymous()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Spa_fallback_stays_anonymous()
    {
        var webRoot = Directory.CreateTempSubdirectory("plantops-wwwroot-").FullName;
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<html>spa</html>", Ct);
        try
        {
            await using var factory = CreateFactory(webRoot: webRoot);
            using var client = factory.CreateClient();

            var route = await client.GetAsync("/assets/123", Ct);
            var file = await client.GetAsync("/index.html", Ct);

            Assert.Equal(HttpStatusCode.OK, route.StatusCode);
            Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Me_is_401_when_anonymous()
    {
        await using var factory = CreateTestAuthFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/identity/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Me_returns_name_and_roles_array_for_a_signed_in_user()
    {
        await using var factory = CreateTestAuthFactory();
        using var client = factory.CreateClient().AsUser("Sam", Roles.Supervisor, Roles.Admin);

        var response = await client.GetAsync("/api/identity/me", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Sam", body.GetProperty("name").GetString());
        Assert.Equal([Roles.Supervisor, Roles.Admin], body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Unsafe_request_without_csrf_header_is_400_before_anything_else()
    {
        await using var factory = CreateTestAuthFactory();
        using var signedIn = factory.CreateClient().AsUser("Sam", Roles.Supervisor);
        using var anonymous = factory.CreateClient();

        var asUser = await signedIn.PostAsJsonAsync("/api/assets", new { }, Ct);
        var asAnonymous = await anonymous.PostAsJsonAsync("/api/assets", new { }, Ct);

        foreach (var response in new[] { asUser, asAnonymous })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("X-CSRF", await response.Content.ReadAsStringAsync(Ct));
        }
    }

    [Fact]
    public async Task Safe_requests_do_not_need_the_csrf_header()
    {
        await using var factory = CreateTestAuthFactory();
        using var client = factory.CreateClient().AsUser("Sam", Roles.Operator);

        var response = await client.GetAsync("/api/identity/me", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Operator_gets_403_on_a_manage_assets_endpoint_before_the_database_is_touched()
    {
        await using var factory = CreateTestAuthFactory();
        using var client = factory.CreateClient().AsUser("Olivia", Roles.Operator).WithCsrf();

        var response = await client.PostAsJsonAsync("/api/assets", new { }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Supervisor_passes_authorization_on_a_manage_assets_endpoint()
    {
        await using var factory = CreateTestAuthFactory();
        using var client = factory.CreateClient().AsUser("Sam", Roles.Supervisor).WithCsrf();

        // The handler rejects the empty payload before it reaches the database; getting past 401/403 is the point.
        // (A body-less POST would 404 instead: the endpoint requires a JSON content type, so routing skips it.)
        var response = await client.PostAsJsonAsync("/api/assets", new { }, Ct);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_rejects_an_external_return_url()
    {
        await using var factory = CreateOidcFactory();
        using var client = NoRedirectClient(factory);

        foreach (var url in new[] { "https://evil.example", "//evil.example", "/\\evil.example", "javascript:alert(1)" })
        {
            var response = await client.GetAsync($"/api/identity/login?returnUrl={Uri.EscapeDataString(url)}", Ct);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{url} gave {response.StatusCode}");
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Login_redirects_to_the_idp_with_pkce_state_and_nonce()
    {
        await using var factory = CreateOidcFactory();
        using var client = NoRedirectClient(factory);

        var response = await client.GetAsync("/api/identity/login?returnUrl=/assets", Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith("https://idp.test/authorize", location.AbsoluteUri, StringComparison.Ordinal);
        var query = System.Web.HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("plantops-web", query["client_id"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrEmpty(query["code_challenge"]));
        Assert.False(string.IsNullOrEmpty(query["state"]));
        Assert.False(string.IsNullOrEmpty(query["nonce"]));
        Assert.Contains("openid", query["scope"]!.Split(' '));
    }

    [Fact]
    public async Task Login_without_idp_configuration_fails_clearly_and_the_app_still_runs()
    {
        await using var factory = CreateFactory();
        using var client = NoRedirectClient(factory);

        var login = await client.GetAsync("/api/identity/login", Ct);
        var live = await client.GetAsync("/health/live", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task Logout_requires_a_session_and_returns_the_idp_logout_url()
    {
        await using var factory = CreateOidcFactory();
        using var anonymous = NoRedirectClient(factory).WithCsrf();
        using var signedIn = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddTestAuthentication()))
            .CreateClient().AsUser("Sam", Roles.Operator).WithCsrf();

        var denied = await anonymous.PostAsync("/api/identity/logout", content: null, Ct);
        var ok = await signedIn.PostAsync("/api/identity/logout", content: null, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await ok.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var logoutUrl = body.GetProperty("logoutUrl").GetString()!;
        Assert.StartsWith("https://idp.test/logout?", logoutUrl, StringComparison.Ordinal);
        Assert.Contains("post_logout_redirect_uri=http%3A%2F%2Flocalhost%2F", logoutUrl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("/assets?x=1", true)]
    [InlineData("~/assets", true)]
    [InlineData("//evil.example", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("~//evil.example", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("assets", false)]
    [InlineData("/a\nb", false)]
    [InlineData("", false)]
    public void IsLocalUrl_follows_url_islocalurl_semantics(string url, bool expected) =>
        Assert.Equal(expected, IdentityEndpoints.IsLocalUrl(url));

    [Fact]
    public void Roles_json_array_maps_to_one_claim_per_role()
    {
        var options = new OpenIdConnectOptions();
        new OidcOptionsSetup(Options.Create(new AuthOptions { Authority = "https://idp.test", ClientId = "c" })).Configure(options);
        // The id_token already contributed a role; userinfo repeats it and adds another.
        var identity = new ClaimsIdentity("oidc", "name", "roles");
        identity.AddClaim(new Claim("roles", Roles.Supervisor));
        using var userInfo = JsonDocument.Parse("""{"sub":"1","name":"Sam","roles":["supervisor","admin"]}""");

        foreach (var action in options.ClaimActions)
        {
            action.Run(userInfo.RootElement, identity, "iss");
        }

        // Duplicates (id_token + userinfo) are harmless: IsInRole doesn't care and /me returns Distinct().
        Assert.Equal([Roles.Admin, Roles.Supervisor], identity.FindAll("roles").Select(c => c.Value).Distinct().Order());
        Assert.True(identity.IsAuthenticated);
        Assert.True(new ClaimsPrincipal(identity).IsInRole(Roles.Admin));
    }
}
