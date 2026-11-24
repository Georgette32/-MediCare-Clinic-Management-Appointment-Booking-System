using System.Reflection;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Tests;

public sealed class WorkingHourExtensionAuthorizationTests
{
    [Theory]
    [InlineData(nameof(DoctorController.CreateExtension))]
    [InlineData(nameof(DoctorController.DecideExtension))]
    [InlineData(nameof(DoctorController.CancelExtension))]
    public void Doctor_extension_mutations_are_posts_with_antiforgery(string actionName)
    {
        AssertPostWithAntiForgery(typeof(DoctorController), actionName);
    }

    [Theory]
    [InlineData(nameof(ReceptionController.RequestExtension))]
    [InlineData(nameof(ReceptionController.CancelExtension))]
    public void Reception_extension_mutations_are_posts_with_antiforgery(string actionName)
    {
        AssertPostWithAntiForgery(typeof(ReceptionController), actionName);
    }

    [Fact]
    public void Doctor_extension_pages_are_doctor_only()
    {
        var authorization = typeof(DoctorController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorization);
        Assert.Equal(MediCareRoles.Doctor, authorization!.Roles);
        Assert.NotNull(typeof(DoctorController).GetMethod(nameof(DoctorController.Extensions)));
    }

    [Fact]
    public void Reception_extension_pages_allow_receptionist_and_admin_only()
    {
        var authorization = typeof(ReceptionController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorization);
        var roles = authorization!.Roles!.Split(',');

        Assert.Contains(MediCareRoles.Receptionist, roles);
        Assert.Contains(MediCareRoles.Admin, roles);
        Assert.DoesNotContain(MediCareRoles.Doctor, roles);
        Assert.DoesNotContain(MediCareRoles.Patient, roles);
        Assert.NotNull(typeof(ReceptionController).GetMethod(nameof(ReceptionController.Extensions)));
    }

    private static void AssertPostWithAntiForgery(Type controller, string actionName)
    {
        var action = controller.GetMethod(actionName);
        Assert.NotNull(action);
        Assert.NotNull(action!.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}
