using Microsoft.AspNetCore.Http;

namespace MediCare.Web.Middleware;

/// <summary>
/// Adds the baseline security headers to every response. A header that an action already set is left as it is
/// (for example attachment downloads set X-Content-Type-Options themselves). Headers are applied when the response
/// starts, so they also reach error responses produced later in the pipeline.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Content-Security-Policy"] = ContentSecurityPolicy,
        ["X-Content-Type-Options"] = "nosniff",
        ["X-Frame-Options"] = "DENY",
        ["Referrer-Policy"] = "strict-origin-when-cross-origin",
        ["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        // Applied now, so an action that sets a header later replaces the default (it is the more specific value).
        Apply(context.Response.Headers);
        // Re-applied when the response starts, so headers are present on error responses written further down the pipeline.
        context.Response.OnStarting(() =>
        {
            Apply(context.Response.Headers);
            return Task.CompletedTask;
        });
        await next(context);
    }

    public static void Apply(IHeaderDictionary headers)
    {
        foreach (var (name, value) in Defaults)
            headers.TryAdd(name, value);
    }
}
