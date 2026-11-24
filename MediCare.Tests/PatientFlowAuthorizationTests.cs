using System.Reflection;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Tests;

public sealed class PatientFlowAuthorizationTests
{
    [Fact]
    public void Patient_appointments_controller_is_patient_only()
    {
        var authorization = typeof(AppointmentsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorization);
        Assert.Equal(MediCareRoles.Patient, authorization!.Roles);
    }

    [Theory]
    [InlineData(nameof(AppointmentsController.Book))]
    [InlineData(nameof(AppointmentsController.Cancel))]
    [InlineData(nameof(AppointmentsController.Reschedule))]
    public void Patient_post_actions_require_antiforgery(string actionName)
    {
        AssertPostRequiresAntiforgery(typeof(AppointmentsController), actionName);
    }

    [Theory]
    [InlineData(nameof(ReceptionController.CancelAppointment))]
    [InlineData(nameof(ReceptionController.BookForPatient))]
    [InlineData(nameof(ReceptionController.RescheduleAppointment))]
    public void Reception_appointment_and_booking_posts_require_antiforgery(string actionName)
    {
        AssertPostRequiresAntiforgery(typeof(ReceptionController), actionName);
    }

    [Theory]
    [InlineData(nameof(ReceptionController.Patients))]
    [InlineData(nameof(ReceptionController.PatientDetail))]
    [InlineData(nameof(ReceptionController.BookForPatient))]
    [InlineData(nameof(ReceptionController.Appointments))]
    [InlineData(nameof(ReceptionController.RescheduleAppointment))]
    public void Reception_patient_and_appointment_pages_exclude_patient_and_doctor_roles(string actionName)
    {
        // The controller-level attribute covers every action, including the new ones; the action itself adds nothing wider.
        var controllerAuthorization = typeof(ReceptionController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(controllerAuthorization);
        var roles = controllerAuthorization!.Roles!.Split(',');
        Assert.Contains(MediCareRoles.Receptionist, roles);
        Assert.Contains(MediCareRoles.Admin, roles);
        Assert.DoesNotContain(MediCareRoles.Patient, roles);
        Assert.DoesNotContain(MediCareRoles.Doctor, roles);

        var actions = typeof(ReceptionController).GetMethods().Where(method => method.Name == actionName).ToArray();
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var actionAuthorization = action.GetCustomAttribute<AuthorizeAttribute>();
            Assert.True(actionAuthorization is null || actionAuthorization.Roles is null || !actionAuthorization.Roles.Contains(MediCareRoles.Patient));
        }
    }

    [Fact]
    public void Profile_pages_require_a_signed_in_user()
    {
        var profile = typeof(AccountController).GetMethod(nameof(AccountController.Profile));
        Assert.NotNull(profile);
        Assert.NotNull(profile!.GetCustomAttribute<AuthorizeAttribute>());

        Assert.Equal(1, typeof(AccountController).GetMethods().Count(method => method.Name == nameof(AccountController.Profile)));
    }

    [Theory]
    [InlineData(nameof(AccountController.ProfileName))]
    [InlineData(nameof(AccountController.ProfileEmail))]
    public void Profile_posts_require_sign_in_and_antiforgery(string actionName)
    {
        var action = typeof(AccountController).GetMethods().Single(method => method.Name == actionName);
        Assert.NotNull(action.GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    private static void AssertPostRequiresAntiforgery(Type controller, string actionName)
    {
        var posts = controller.GetMethods()
            .Where(method => method.Name == actionName && method.GetCustomAttribute<HttpPostAttribute>() is not null)
            .ToArray();
        Assert.Single(posts);
        Assert.NotNull(posts[0].GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}
