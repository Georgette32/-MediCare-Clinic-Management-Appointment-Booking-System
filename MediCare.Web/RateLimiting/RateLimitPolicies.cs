using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using System.Threading.RateLimiting;

namespace MediCare.Web.RateLimiting;

public static class RateLimitPolicies
{
    /// <summary>Sign-in and password-change POSTs.</summary>
    public const string Auth = "auth";

    /// <summary>Self-service patient registration POST.</summary>
    public const string Register = "register";

    public const string TooManyAttemptsPage = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>Too many attempts - MediCare</title>
            <link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css" />
        </head>
        <body class="bg-light">
            <main class="container py-5" style="max-width: 36rem">
                <h1 class="h3 mb-3">Too many attempts</h1>
                <p>Too many attempts. Please wait a minute and try again.</p>
                <a class="btn btn-outline-primary" href="/Account/Login">Back to sign in</a>
            </main>
        </body>
        </html>
        """;

    public static IServiceCollection AddMediCareRateLimiting(this IServiceCollection services, RateLimitingOptions limits)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy<string>(Auth, context => FixedWindowFor(context, limits.Auth));
            limiter.AddPolicy<string>(Register, context => FixedWindowFor(context, limits.Register));
            limiter.OnRejected = async (rejected, cancellationToken) =>
            {
                var response = rejected.HttpContext.Response;
                if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    response.Headers[HeaderNames.RetryAfter] = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                response.ContentType = "text/html; charset=utf-8";
                await response.WriteAsync(TooManyAttemptsPage, cancellationToken);
            };
        });
        return services;
    }

    private static RateLimitPartition<string> FixedWindowFor(HttpContext context, RateLimitRule rule) =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientAddress(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rule.PermitLimit,
                Window = TimeSpan.FromSeconds(rule.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            });

    /// <summary>Partition key: the connecting client address. Requests without one share a single bucket.</summary>
    public static string ClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
