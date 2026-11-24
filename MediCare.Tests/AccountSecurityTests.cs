using MediCare.Infrastructure.Identity;
using MediCare.Web.Controllers;
using MediCare.Web.Filters;
using Microsoft.Extensions.Configuration;

namespace MediCare.Tests;

public sealed class AccountSecurityTests
{
    [Fact]
    public void Bootstrap_admin_configuration_is_required_and_has_no_credential_defaults()
    {
        var configuration = new ConfigurationBuilder().Build();
        var exception = Assert.Throws<InvalidOperationException>(() => InitialAdminSettings.FromConfiguration(configuration));
        Assert.Contains("BootstrapAdmin:Email", exception.Message);
    }

    [Theory]
    [InlineData("/Home/Index", true, true)]
    [InlineData("/Account/Login", true, true)]
    [InlineData("/Account/ChangePassword", true, false)]
    [InlineData("/Account/Logout", true, false)]
    [InlineData("/css/site.css", true, false)]
    [InlineData("/Reports/export.csv", true, true)]
    [InlineData("/Home/Index", false, false)]
    public void Password_gate_redirects_until_change_except_password_change_logout_and_static_files(string path, bool mustChange, bool expected)
    {
        Assert.Equal(expected, MustChangePasswordPolicy.ShouldRedirect(mustChange, new Microsoft.AspNetCore.Http.PathString(path)));
    }

    [Fact]
    public void Password_change_post_requires_antiforgery()
    {
        var method = typeof(AccountController).GetMethod(nameof(AccountController.ChangePassword), [typeof(MediCare.Web.Models.ChangePasswordViewModel)]);
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute), false).SingleOrDefault());
    }
}
