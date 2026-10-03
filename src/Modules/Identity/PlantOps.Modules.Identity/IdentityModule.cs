using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PlantOps.BuildingBlocks.Infrastructure;
using PlantOps.Modules.Identity.Contracts;

namespace PlantOps.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        // BindConfiguration reads lazily (see OidcOptionsSetup); the parameter is kept for the module convention.
        services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.Section);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<OpenIdConnectOptions>, OidcOptionsSetup>());

        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "__Host-plantops";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                // Lax, not Strict: the redirect back from the IdP is a cross-site top-level navigation.
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = context => RespondOrRedirect(context, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = context => RespondOrRedirect(context, StatusCodes.Status403Forbidden);
            })
            .AddOpenIdConnect();

        services.AddAuthorizationBuilder()
            // Secure by default: an endpoint nobody annotated still requires a signed-in user.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.ManageAssets, policy => policy.RequireRole(Roles.Supervisor, Roles.Admin))
            .AddPolicy(Policies.SuperviseWorkOrders, policy => policy.RequireRole(Roles.Supervisor, Roles.Admin));

        services.AddDbContext<IdentityDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("PlantOps"),
            sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema);
                sql.EnableRetryOnFailure();
            }));
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<UserProvisioner>();
        services.AddMigrateOnStartup<IdentityDbContext>();
        services.AddHealthChecks().AddDbContextCheck<IdentityDbContext>("identity-db", tags: ["ready"]);

        return services;
    }

    /// <summary>CSRF guard, then authentication, then authorization. Call after static files, before mapping endpoints.</summary>
    public static IApplicationBuilder UseIdentityModule(this IApplicationBuilder app)
    {
        app.UseMiddleware<CsrfGuardMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity").WithTags("Identity");
        IdentityEndpoints.Map(group);
        return app;
    }

    // API callers get a status code, never a 302 to an HTML login page.
    private static Task RespondOrRedirect(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = status;
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }

        return Task.CompletedTask;
    }
}
