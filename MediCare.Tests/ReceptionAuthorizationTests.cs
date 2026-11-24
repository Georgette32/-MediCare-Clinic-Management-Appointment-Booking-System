using System.Reflection;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Tests;

public sealed class ReceptionAuthorizationTests
{
    [Fact]
    public void Reception_controller_allows_only_receptionist_and_admin_roles()
    {
        var authorization = typeof(ReceptionController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorization);
        var allowedRoles = authorization!.Roles!.Split(',');

        Assert.Contains(MediCareRoles.Receptionist, allowedRoles);
        Assert.Contains(MediCareRoles.Admin, allowedRoles);
        Assert.DoesNotContain(MediCareRoles.Patient, allowedRoles);
        Assert.DoesNotContain(MediCareRoles.Doctor, allowedRoles);
    }

    [Theory]
    [InlineData(nameof(ReceptionController.ScheduledCheckIn))]
    [InlineData(nameof(ReceptionController.RegisterWalkIn))]
    public void Check_in_post_actions_require_antiforgery(string actionName)
    {
        var action = typeof(ReceptionController).GetMethod(actionName);
        Assert.NotNull(action);
        Assert.NotNull(action!.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void Doctor_approval_controller_is_doctor_only_and_reception_roles_are_not_decision_roles()
    {
        var authorization = typeof(DoctorController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorization);
        Assert.Equal(MediCareRoles.Doctor, authorization!.Roles);
        Assert.NotEqual(MediCareRoles.Receptionist, authorization.Roles);
        Assert.NotEqual(MediCareRoles.Admin, authorization.Roles);
        Assert.NotEqual(MediCareRoles.Patient, authorization.Roles);
    }

    [Theory]
    [InlineData(nameof(ReceptionController.RequestWalkInAdmission))]
    [InlineData(nameof(ReceptionController.CallWalkIn))]
    public void Admission_request_and_call_post_actions_require_antiforgery(string actionName)
    {
        var action = typeof(ReceptionController).GetMethod(actionName);
        Assert.NotNull(action);
        Assert.NotNull(action!.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void Doctor_decision_post_requires_antiforgery()
    {
        var action = typeof(DoctorController).GetMethod(nameof(DoctorController.DecideWalkInAdmission));
        Assert.NotNull(action);
        Assert.NotNull(action!.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}
