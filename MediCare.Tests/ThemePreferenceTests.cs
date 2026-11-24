using MediCare.Web.Controllers;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

namespace MediCare.Tests;

public class ThemePreferenceTests
{
    [Theory]
    [InlineData("light", "light")]
    [InlineData("DARK", "dark")]
    [InlineData(" auto ", "auto")]
    [InlineData(null, "auto")]
    [InlineData("", "auto")]
    [InlineData("\" onload=\"x", "auto")]
    [InlineData("lobby", "auto")]
    public void Normalize_accepts_only_the_three_themes(string? input, string expected)
    {
        Assert.Equal(expected, ThemePreference.Normalize(input));
    }

    [Fact]
    public void Set_saves_the_theme_cookie_and_returns_to_the_local_page()
    {
        var (controller, context) = CreateController();

        var result = controller.Set("dark", "/Reception/Queue?doctorId=1");

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/Reception/Queue?doctorId=1", redirect.Url);
        var cookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains($"{ThemePreference.CookieName}=dark", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData(null)]
    public void Set_never_redirects_off_site(string? returnUrl)
    {
        var (controller, _) = CreateController();

        var result = controller.Set("light", returnUrl);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Home", redirect.ControllerName);
        Assert.Equal("Index", redirect.ActionName);
    }

    [Fact]
    public void Set_stores_auto_for_an_unknown_theme()
    {
        var (controller, context) = CreateController();

        controller.Set("purple", "/");

        Assert.Contains($"{ThemePreference.CookieName}=auto", context.Response.Headers.SetCookie.ToString(), StringComparison.Ordinal);
    }

    private static (ThemeController Controller, DefaultHttpContext Context) CreateController()
    {
        var context = new DefaultHttpContext();
        var controller = new ThemeController { ControllerContext = new ControllerContext { HttpContext = context } };
        controller.Url = new UrlHelper(new ActionContext(context, new RouteData(), new ActionDescriptor()));
        return (controller, context);
    }
}
