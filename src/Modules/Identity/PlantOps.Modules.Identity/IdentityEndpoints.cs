using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PlantOps.Modules.Identity;

internal static class IdentityEndpoints
{
    public const string LoginPath = "/api/identity/login";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/login", Login).AllowAnonymous();
        // Anonymous so it can answer 401 itself; the SPA uses that answer to decide whether to start the login.
        group.MapGet("/me", Me).AllowAnonymous();
        // POST: a GET logout could be triggered by any <img src> on another site.
        group.MapPost("/logout", Logout);
    }

    private static IResult Login(string? returnUrl, HttpContext context, IOptions<AuthOptions> auth)
    {
        returnUrl = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
        if (!IsLocalUrl(returnUrl))
        {
            // Otherwise this endpoint is an open redirect, a phishing helper (OWASP A01).
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad request",
                detail: "returnUrl must be a local URL.");
        }

        if (returnUrl.StartsWith("~/", StringComparison.Ordinal))
        {
            returnUrl = returnUrl[1..];
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            return Results.LocalRedirect(returnUrl);
        }

        if (!auth.Value.IsConfigured)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Sign-in unavailable",
                detail: "Sign-in is not configured on this server (Auth:Authority is missing).");
        }

        return Results.Challenge(
            new AuthenticationProperties { RedirectUri = returnUrl },
            [OpenIdConnectDefaults.AuthenticationScheme]);
    }

    private static IResult Me(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized");
        }

        var roles = user.FindAll(Claims.Roles).Select(c => c.Value).Distinct().ToArray();
        return Results.Ok(new MeResponse(
            user.FindFirstValue(Claims.Name) ?? user.Identity.Name,
            user.FindFirstValue(Claims.Email),
            roles));
    }

    private static async Task<IResult> Logout(
        HttpContext context,
        IOptionsMonitor<OpenIdConnectOptions> oidc,
        ILoggerFactory loggerFactory)
    {
        var cookie = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var idToken = cookie.Properties?.GetTokenValue("id_token");
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // We return the IdP URL instead of redirecting: a fetch() that follows a cross-origin 302 hits CORS.
        string? endSession = null;
        var manager = oidc.Get(OpenIdConnectDefaults.AuthenticationScheme).ConfigurationManager;
        if (manager is not null)
        {
            try
            {
                endSession = (await manager.GetConfigurationAsync(context.RequestAborted)).EndSessionEndpoint;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The local session is already gone; an unreachable IdP must not turn sign-out into an error.
                loggerFactory.CreateLogger(typeof(IdentityEndpoints))
                    .LogWarning(ex, "Could not load the OIDC configuration; signing out locally only.");
            }
        }

        if (string.IsNullOrEmpty(endSession))
        {
            return Results.Ok(new LogoutResponse("/"));
        }

        var query = new Dictionary<string, string?>
        {
            ["post_logout_redirect_uri"] = $"{context.Request.Scheme}://{context.Request.Host}/",
        };
        if (idToken is not null)
        {
            query["id_token_hint"] = idToken;
        }

        return Results.Ok(new LogoutResponse(QueryHelpers.AddQueryString(endSession, query)));
    }

    // Same rules as Url.IsLocalUrl, which lives in MVC and is not available to minimal APIs.
    internal static bool IsLocalUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.Any(char.IsControl))
        {
            return false;
        }

        var rest = url[0] switch
        {
            '/' => url[1..],
            '~' when url.Length > 1 && url[1] == '/' => url[2..],
            _ => null,
        };

        // "//host" and "/\host" are scheme-relative URLs that browsers resolve to another site.
        return rest is not null && (rest.Length == 0 || (rest[0] != '/' && rest[0] != '\\'));
    }
}

internal sealed record MeResponse(string? Name, string? Email, string[] Roles);

internal sealed record LogoutResponse(string LogoutUrl);
