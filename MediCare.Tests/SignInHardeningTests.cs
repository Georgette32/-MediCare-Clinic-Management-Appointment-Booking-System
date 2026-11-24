using System.Text.RegularExpressions;
using MediCare.Web.Middleware;
using MediCare.Web.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace MediCare.Tests;

public sealed class SignInHardeningTests
{
    private static RateLimitingOptions Limits(int authPermit, int authWindow, int registerPermit, int registerWindow) => new()
    {
        Auth = new RateLimitRule { PermitLimit = authPermit, WindowSeconds = authWindow },
        Register = new RateLimitRule { PermitLimit = registerPermit, WindowSeconds = registerWindow }
    };

    [Fact]
    public void Rate_limit_values_from_configuration_bind_and_validate()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Auth:PermitLimit"] = "10",
            ["RateLimiting:Auth:WindowSeconds"] = "60",
            ["RateLimiting:Register:PermitLimit"] = "5",
            ["RateLimiting:Register:WindowSeconds"] = "3600"
        }).Build();

        var options = RateLimitingOptions.Load(configuration);
        Assert.Equal(10, options.Auth.PermitLimit);
        Assert.Equal(60, options.Auth.WindowSeconds);
        Assert.Equal(5, options.Register.PermitLimit);
        Assert.Equal(3600, options.Register.WindowSeconds);
    }

    [Fact]
    public void Missing_rate_limit_configuration_stops_startup_with_the_missing_keys_named()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => RateLimitingOptions.Load(new ConfigurationBuilder().Build()));
        Assert.Contains("RateLimiting:Auth:PermitLimit", exception.Message);
        Assert.Contains("RateLimiting:Register:WindowSeconds", exception.Message);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(-1, 60)]
    [InlineData(10, 0)]
    [InlineData(10, -5)]
    public void Rate_limit_values_must_be_greater_than_zero(int permit, int window)
    {
        var errors = RateLimitingOptions.Validate(Limits(permit, window, 5, 3600));
        Assert.Single(errors);
        Assert.Contains("RateLimiting:Auth", errors[0]);
        Assert.Empty(RateLimitingOptions.Validate(Limits(1, 1, 1, 1)));
    }

    [Fact]
    public void Shipped_appsettings_contain_the_agreed_rate_limits()
    {
        var path = FindWebFile("appsettings.json");
        Assert.NotNull(path);
        var configuration = new ConfigurationBuilder().AddJsonFile(path!).Build();

        var options = RateLimitingOptions.Load(configuration);
        Assert.Equal(10, options.Auth.PermitLimit);
        Assert.Equal(60, options.Auth.WindowSeconds);
        Assert.Equal(5, options.Register.PermitLimit);
        Assert.Equal(3600, options.Register.WindowSeconds);
    }

    [Fact]
    public void Rate_limit_page_is_a_friendly_html_message()
    {
        Assert.Contains("Too many attempts. Please wait a minute and try again.", RateLimitPolicies.TooManyAttemptsPage);
        Assert.DoesNotContain("<script", RateLimitPolicies.TooManyAttemptsPage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Security_headers_are_added_with_the_agreed_values()
    {
        var headers = new HeaderDictionary();
        SecurityHeadersMiddleware.Apply(headers);

        Assert.Equal("default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'", headers["Content-Security-Policy"].ToString());
        Assert.Equal("nosniff", headers["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", headers["X-Frame-Options"].ToString());
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"].ToString());
        Assert.Equal("camera=(), microphone=(), geolocation=()", headers["Permissions-Policy"].ToString());
    }

    [Fact]
    public void Security_headers_do_not_overwrite_headers_an_action_already_set()
    {
        var headers = new HeaderDictionary
        {
            ["X-Content-Type-Options"] = "custom-value",
            ["X-Frame-Options"] = "SAMEORIGIN"
        };
        SecurityHeadersMiddleware.Apply(headers);

        Assert.Equal("custom-value", headers["X-Content-Type-Options"].ToString());
        Assert.Equal("SAMEORIGIN", headers["X-Frame-Options"].ToString());
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"].ToString());
    }

    [Fact]
    public async Task Security_headers_middleware_applies_headers_when_the_response_starts_and_keeps_action_headers()
    {
        var context = new DefaultHttpContext();
        var middleware = new SecurityHeadersMiddleware(httpContext =>
        {
            httpContext.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        // The server starts the response after the pipeline runs; StartAsync fires the OnStarting callbacks the same way.
        await context.Response.StartAsync();

        Assert.Equal("SAMEORIGIN", context.Response.Headers["X-Frame-Options"].ToString());
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Contains("frame-ancestors 'none'", context.Response.Headers["Content-Security-Policy"].ToString());
    }

    [Fact]
    public void Auth_cookie_is_http_only_lax_and_secure_outside_development()
    {
        var production = new CookieBuilder();
        AuthCookiePolicy.Apply(production, isDevelopment: false);
        Assert.True(production.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, production.SameSite);
        Assert.Equal(CookieSecurePolicy.Always, production.SecurePolicy);

        var development = new CookieBuilder();
        AuthCookiePolicy.Apply(development, isDevelopment: true);
        Assert.True(development.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, development.SameSite);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, development.SecurePolicy);
    }

    [Fact]
    public void Views_have_no_inline_script_blocks_or_inline_event_handlers()
    {
        var views = FindWebDirectory("Views");
        Assert.NotNull(views);

        var inlineScript = new Regex(@"<script(?![^>]*\ssrc\s*=)[^>]*>", RegexOptions.IgnoreCase);
        var inlineHandler = new Regex(@"\son[a-z]+\s*=", RegexOptions.IgnoreCase);
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(views!, "*.cshtml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (inlineScript.IsMatch(text) || inlineHandler.IsMatch(text))
                offenders.Add(Path.GetRelativePath(views!, file));
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Views_load_scripts_only_from_local_files()
    {
        var views = FindWebDirectory("Views");
        Assert.NotNull(views);

        var remoteScript = new Regex(@"<script[^>]*\ssrc\s*=\s*[""']https?:", RegexOptions.IgnoreCase);
        foreach (var file in Directory.EnumerateFiles(views!, "*.cshtml", SearchOption.AllDirectories))
            Assert.False(remoteScript.IsMatch(File.ReadAllText(file)), $"Remote script in {file}");
    }

    /// <summary>
    /// Regression (found in the browser): the print layout did not load site.js, so the data-print / data-close-window
    /// buttons stopped working once inline handlers were removed for the CSP. Every layout must load site.js.
    /// </summary>
    [Fact]
    public void Every_layout_loads_site_js_for_data_attribute_behaviour()
    {
        var views = FindWebDirectory("Views");
        Assert.NotNull(views);

        var layouts = Directory.EnumerateFiles(views!, "*.cshtml", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("@RenderBody()", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(layouts);
        foreach (var layout in layouts)
            Assert.True(File.ReadAllText(layout).Contains("~/js/site.js", StringComparison.Ordinal), $"{layout} does not load site.js");
    }

    private static string? FindWebFile(string fileName)
    {
        var directory = FindWebDirectory(string.Empty);
        return directory is null ? null : Path.Combine(directory, fileName);
    }

    private static string? FindWebDirectory(string child)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var web = Path.Combine(directory.FullName, "MediCare.Web");
            if (Directory.Exists(web))
            {
                var candidate = string.IsNullOrEmpty(child) ? web : Path.Combine(web, child);
                return Directory.Exists(candidate) || File.Exists(candidate) ? candidate : null;
            }
        }
        return null;
    }
}
