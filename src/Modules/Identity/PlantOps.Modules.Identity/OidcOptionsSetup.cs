using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PlantOps.BuildingBlocks.Infrastructure;

namespace PlantOps.Modules.Identity;

// An IConfigureOptions rather than a lambda in AddOpenIdConnect: it runs when the options are first resolved,
// so configuration added after service registration (WebApplicationFactory, user-secrets) is honoured.
internal sealed class OidcOptionsSetup(IOptions<AuthOptions> auth) : IConfigureNamedOptions<OpenIdConnectOptions>
{
    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name != OpenIdConnectDefaults.AuthenticationScheme)
        {
            return;
        }

        Configure(options);
    }

    public void Configure(OpenIdConnectOptions options)
    {
        var settings = auth.Value;

        options.Authority = settings.Authority;
        options.MetadataAddress = settings.MetadataAddress;
        options.ClientId = settings.ClientId;
        options.ClientSecret = settings.ClientSecret;
        options.RequireHttpsMetadata = settings.RequireHttpsMetadata;

        if (!settings.IsConfigured)
        {
            // The OIDC handler is a request handler, so the framework validates its options on EVERY request.
            // Without an authority that validation would throw and take the whole app (health checks, SPA) down.
            // A placeholder keeps the host alive; /api/identity/login reports the missing configuration clearly.
            options.ClientId = string.IsNullOrWhiteSpace(settings.ClientId) ? "unconfigured" : settings.ClientId;
            options.Configuration = new OpenIdConnectConfiguration();
        }

        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        // Query, not the default form_post: the callback is then a top-level GET, so the correlation and nonce
        // cookies can be SameSite=Lax. form_post is a cross-site POST that needs SameSite=None, and browsers
        // drop SameSite=None cookies without Secure (so login would fail on plain-http localhost).
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SameSite = SameSiteMode.Lax;

        // The id_token is kept in the (encrypted) auth cookie only so logout can send id_token_hint to the IdP.
        // It never reaches the browser in readable form.
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        // Keep claim names exactly as the IdP sends them ("roles", not a long WS-* URI).
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = Claims.Roles;

        // The default ClaimActions don't know "roles". MapJsonKey emits one claim per array element;
        // MapUniqueJsonKey would NOT (it stores the raw '["a","b"]' text as one claim, so IsInRole fails).
        options.ClaimActions.MapJsonKey(Claims.Roles, Claims.Roles);

        // OnTicketReceived, not OnTokenValidated: the handler validates the id_token (OnTokenValidated), THEN fetches the
        // userinfo endpoint and runs the claim actions that add "roles" and "email" (verified in OpenIdConnectHandler:
        // RunTokenValidatedEventAsync precedes GetUserInformationAsync). Only the ticket event, raised after the whole
        // remote flow and before the cookie is issued, sees the final principal.
        options.Events.OnTicketReceived = async context =>
        {
            var services = context.HttpContext.RequestServices;
            await services.GetRequiredService<UserProvisioner>()
                .UpsertAsync(context.Principal, context.HttpContext.RequestAborted);

            // Only the subject id is logged: no name, email or token (security event 1001).
            if (context.Principal?.FindFirstValue(Claims.Subject) is { Length: > 0 } subject)
            {
                SecurityEvents.SignInSucceeded(SecurityLogger(services), subject);
            }
        };

        // Neither handler marks the failure as handled: the framework's default response stays as it was, we only
        // make the failure visible. The reason is the exception type, never its message (it can echo IdP output).
        options.Events.OnRemoteFailure = context =>
        {
            SecurityEvents.SignInFailed(SecurityLogger(context.HttpContext.RequestServices), FailureReason(context.Failure));
            return Task.CompletedTask;
        };
        options.Events.OnAuthenticationFailed = context =>
        {
            SecurityEvents.SignInFailed(SecurityLogger(context.HttpContext.RequestServices), FailureReason(context.Exception));
            return Task.CompletedTask;
        };

        options.Events.OnRedirectToIdentityProvider = context =>
        {
            // An XHR must never be redirected to the IdP: it would receive the login page's HTML instead of JSON.
            // Only the explicit login endpoint (a full-page navigation) may start the flow.
            var path = context.Request.Path;
            if (path.StartsWithSegments("/api") && !path.StartsWithSegments(IdentityEndpoints.LoginPath))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.HandleResponse();
            }

            return Task.CompletedTask;
        };
    }

    internal static ILogger SecurityLogger(IServiceProvider services) =>
        services.GetRequiredService<ILoggerFactory>().CreateLogger(SecurityEvents.Category);

    private static string FailureReason(Exception? failure) => failure?.GetType().Name ?? "Unknown";
}
