using System.Reflection;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Tests;

public sealed class ProvisioningAuthorizationTests
{
    [Fact]
    public void Admin_account_provisioning_is_admin_only()
    {
        var authorization = typeof(AdminAccountsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorization);
        Assert.Equal(MediCareRoles.Admin, authorization!.Roles);
    }

    [Theory]
    [InlineData(nameof(AdminAccountsController.Doctor))]
    [InlineData(nameof(AdminAccountsController.Receptionist))]
    public void Admin_account_creation_posts_require_antiforgery(string actionName)
    {
        var action = typeof(AdminAccountsController).GetMethods()
            .Single(method => method.Name == actionName && method.GetCustomAttribute<HttpPostAttribute>() is not null);
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void Patient_account_creation_post_requires_antiforgery_and_receptionist_role()
    {
        var authorization = typeof(ReceptionController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorization);
        Assert.Contains(MediCareRoles.Receptionist, authorization!.Roles!.Split(','));
        Assert.Contains(MediCareRoles.Admin, authorization.Roles.Split(','));

        var action = typeof(ReceptionController).GetMethod(nameof(ReceptionController.CreatePatient), [typeof(MediCare.Web.Models.PatientAccountViewModel), typeof(CancellationToken)]);
        Assert.NotNull(action);
        Assert.Equal(MediCareRoles.Receptionist, action!.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.NotNull(action!.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}
