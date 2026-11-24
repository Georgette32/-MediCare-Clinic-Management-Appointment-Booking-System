using System.Reflection;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Controllers;
using MediCare.Web.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Tests;

public sealed class MedicalAuthorizationTests
{
    [Fact]
    public void Doctor_medical_pages_are_doctor_only_and_not_cacheable()
    {
        var authorization = typeof(MedicalRecordsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorization);
        Assert.Equal(MediCareRoles.Doctor, authorization!.Roles);
        Assert.NotNull(typeof(MedicalRecordsController).GetCustomAttribute<MedicalNoStoreAttribute>());
    }

    [Fact]
    public void Patient_medical_pages_are_patient_only_and_not_cacheable()
    {
        var authorization = typeof(MedicalController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorization);
        Assert.Equal(MediCareRoles.Patient, authorization!.Roles);
        Assert.NotNull(typeof(MedicalController).GetCustomAttribute<MedicalNoStoreAttribute>());
    }

    [Theory]
    [InlineData(nameof(MedicalRecordsController.SaveVisit))]
    [InlineData(nameof(MedicalRecordsController.SavePrescription))]
    [InlineData(nameof(MedicalRecordsController.AddAddendum))]
    public void Medical_writes_are_posts_with_antiforgery(string actionName)
    {
        var action = typeof(MedicalRecordsController).GetMethods().Single(method => method.Name == actionName);

        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Theory]
    [InlineData(nameof(MedicalRecordsController.Visit), "Doctor/Visit/{queueEntryId:guid}")]
    [InlineData(nameof(MedicalRecordsController.PatientHistory), "Doctor/Patient/{patientId:guid}/History")]
    [InlineData(nameof(MedicalRecordsController.Prescription), "Doctor/Prescription/{recordId:guid}")]
    public void Doctor_medical_routes_carry_only_identifiers_in_the_url(string actionName, string template)
    {
        var action = typeof(MedicalRecordsController).GetMethods().Single(method => method.Name == actionName);

        var route = action.GetCustomAttributes().OfType<HttpGetAttribute>().Single();
        Assert.Equal(template, route.Template);
    }

    [Fact]
    public void Receptionists_and_admins_are_not_granted_the_medical_controllers()
    {
        foreach (var controller in new[] { typeof(MedicalRecordsController), typeof(MedicalController) })
        {
            var roles = controller.GetCustomAttribute<AuthorizeAttribute>()!.Roles!.Split(',');
            Assert.DoesNotContain(MediCareRoles.Receptionist, roles);
            Assert.DoesNotContain(MediCareRoles.Admin, roles);
        }
    }

    [Fact]
    public void Doctor_queue_takes_the_medical_service_for_today_visit_links()
    {
        var parameters = typeof(DoctorController).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType);
        Assert.Contains(typeof(MediCare.Application.Services.IMedicalRecordService), parameters);
    }
}
