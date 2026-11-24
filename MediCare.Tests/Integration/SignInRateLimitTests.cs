using System.Net;

namespace MediCare.Tests.Integration;

/// <summary>Sign-in POSTs are limited per client; this uses its own host with a small limit.</summary>
public sealed class SignInRateLimitTests
{
    [Fact]
    public async Task Fourth_sign_in_post_inside_the_window_returns_429_with_a_friendly_page()
    {
        using var factory = new MediCareWebApplicationFactory(authPermitLimit: 3, authWindowSeconds: 60);
        var session = PageSession.Anonymous(factory);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            // Wrong credentials: the form is re-rendered (200), but the attempt still counts against the limit.
            var response = await session.PostFormAsync(
                "/Account/Login",
                "/Account/Login",
                [new("UserName", "nobody-" + attempt + "@example.invalid"), new("Password", "Wrong-Pa55word-here!"), new("RememberMe", "false")]);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var limited = await session.PostFormAsync(
            "/Account/Login",
            "/Account/Login",
            [new("UserName", "nobody-4@example.invalid"), new("Password", "Wrong-Pa55word-here!"), new("RememberMe", "false")]);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"), "The 429 response should say when to retry.");
        Assert.Contains("Too many attempts", await limited.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
