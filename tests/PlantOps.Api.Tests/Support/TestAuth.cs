using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PlantOps.Api.Tests;

// Linked into PlantOps.Modules.Assets.Tests (<Compile Include=... Link=...>) instead of a third test-utils
// project: it is one small file, and a project would add a solution entry and a reference for nothing.
public static class TestAuth
{
    public const string Scheme = "Test";
    public const string Header = "X-Test-User";

    /// <summary>
    /// Replaces ONLY the authentication step. The fallback policy, the named policies, the CSRF guard and the
    /// 401/403 behaviour all still run as in production.
    /// </summary>
    public static IServiceCollection AddTestAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = Scheme;
                options.DefaultChallengeScheme = Scheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(Scheme, _ => { });
        return services;
    }

    /// <summary>Signs in with the subject id derived from the name ("Sam" becomes "sam").</summary>
    public static HttpClient AsUser(this HttpClient client, string name, params string[] roles) =>
        client.AsUserWithId(IdFromName(name), name, roles);

    /// <summary>Signs in with an explicit subject id, e.g. to match a user row seeded in the identity directory.</summary>
    public static HttpClient AsUserWithId(this HttpClient client, string id, string name, params string[] roles)
    {
        client.DefaultRequestHeaders.Remove(Header);
        client.DefaultRequestHeaders.Add(Header, $"id={id};name={name};roles={string.Join(',', roles)}");
        return client;
    }

    internal static string IdFromName(string name) => name.Trim().ToLowerInvariant().Replace(' ', '-');

    public static HttpClient WithCsrf(this HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("X-CSRF");
        client.DefaultRequestHeaders.Add("X-CSRF", "1");
        return client;
    }
}

// Header format: "id=sam;name=Sam;roles=supervisor,admin". "id" becomes the "sub" claim (as the IdP's subject does in
// production) and defaults to the lower-cased name, so ICurrentUser works in tests.
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TestAuth.Header, out var header))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Same claim types as production (OidcOptionsSetup): "name" and "roles".
        var identity = new ClaimsIdentity(TestAuth.Scheme, nameType: "name", roleType: "roles");
        string? id = null;
        string? name = null;
        foreach (var part in header.ToString().Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            switch (pair[0].Trim())
            {
                case "id":
                    id = pair[1];
                    break;
                case "name":
                    name = pair[1];
                    identity.AddClaim(new Claim("name", pair[1]));
                    break;
                case "roles":
                    foreach (var role in pair[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        identity.AddClaim(new Claim("roles", role.Trim()));
                    }

                    break;
            }
        }

        var subject = id ?? (name is null ? null : TestAuth.IdFromName(name));
        if (subject is not null)
        {
            identity.AddClaim(new Claim("sub", subject));
        }

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), TestAuth.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
