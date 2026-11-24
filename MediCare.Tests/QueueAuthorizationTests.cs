using System.Reflection;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Tests;

public sealed class QueueAuthorizationTests
{
    [Theory]
    [InlineData(nameof(ReceptionController.QueueCheckIn))]
    [InlineData(nameof(ReceptionController.QueueRegisterWalkIn))]
    [InlineData(nameof(ReceptionController.QueueRequestApproval))]
    [InlineData(nameof(ReceptionController.QueueCallWalkIn))]
    [InlineData(nameof(ReceptionController.QueueCallScheduled))]
    [InlineData(nameof(ReceptionController.QueueCallNext))]
    [InlineData(nameof(ReceptionController.QueueReturnToWaiting))]
    [InlineData(nameof(ReceptionController.QueueCancel))]
    public void Reception_queue_post_actions_require_post_and_antiforgery(string actionName)
    {
        AssertPostWithAntiforgery(typeof(ReceptionController), actionName);
    }

    [Fact]
    public void Reception_queue_page_is_limited_to_receptionist_and_admin()
    {
        var method = typeof(ReceptionController).GetMethod(nameof(ReceptionController.Queue));
        Assert.NotNull(method);
        Assert.Null(method!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(method.GetCustomAttribute<AuthorizeAttribute>());

        var classRoles = typeof(ReceptionController).GetCustomAttribute<AuthorizeAttribute>()!.Roles!.Split(',');
        Assert.Contains(MediCareRoles.Receptionist, classRoles);
        Assert.Contains(MediCareRoles.Admin, classRoles);
        Assert.DoesNotContain(MediCareRoles.Doctor, classRoles);
    }

    [Theory]
    [InlineData(nameof(DoctorController.StartConsultation))]
    [InlineData(nameof(DoctorController.CompleteConsultation))]
    public void Doctor_consultation_post_actions_require_post_and_antiforgery(string actionName)
    {
        AssertPostWithAntiforgery(typeof(DoctorController), actionName);
    }

    [Fact]
    public void Doctor_queue_page_is_doctor_only_through_the_controller_attribute()
    {
        var authorization = typeof(DoctorController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorization);
        Assert.Equal(MediCareRoles.Doctor, authorization!.Roles);
        Assert.Null(typeof(DoctorController).GetMethod(nameof(DoctorController.Queue))!.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    private static void AssertPostWithAntiforgery(Type controller, string actionName)
    {
        var action = controller.GetMethod(actionName);
        Assert.NotNull(action);
        Assert.NotNull(action!.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}
