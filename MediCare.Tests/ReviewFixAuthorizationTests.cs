using System.Reflection;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Tests;

/// <summary>Authorization attributes added by the business-review fixes (items 10 and 11).</summary>
public sealed class ReviewFixAuthorizationTests
{
    [Fact]
    public void Access_denied_page_is_reachable_anonymously_and_by_get()
    {
        var method = typeof(AccountController).GetMethod(nameof(AccountController.AccessDenied));

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(method.GetCustomAttribute<HttpGetAttribute>());
    }

    [Fact]
    public void Doctor_deactivation_confirm_page_is_admin_only_and_get()
    {
        var controllerAuthorization = typeof(AdminController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(MediCareRoles.Admin, controllerAuthorization!.Roles);

        var method = typeof(AdminController).GetMethod(nameof(AdminController.ConfirmDeactivateStaff));
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void Deactivation_post_keeps_antiforgery_protection()
    {
        var method = typeof(AdminController).GetMethod(nameof(AdminController.SetStaffActive));

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}
