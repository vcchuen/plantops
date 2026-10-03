namespace PlantOps.Api.Security;

// Browser hardening headers on EVERY response: API, static files, the SPA fallback, and error responses (design 08,
// Decision 3). Set in OnStarting rather than before next(): the exception handler clears the response headers when it
// rewrites a failure, so headers set up front would vanish exactly on the error path.
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    // script-src 'self' (no inline script, no eval) is the directive that actually stops injected script running.
    // style-src needs 'unsafe-inline' because Angular injects component styles as <style> elements; inline styles
    // cannot execute code. The Google Fonts origins are there because Angular Material's setup linked them.
    internal const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; img-src 'self' data:; connect-src 'self'; " +
        "frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpContext)state).Response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            return Task.CompletedTask;
        }, context);

        return next(context);
    }
}
