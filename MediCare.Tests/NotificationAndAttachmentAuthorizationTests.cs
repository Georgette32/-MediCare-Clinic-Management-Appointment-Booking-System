using System.Reflection;
using System.Security.Claims;
using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Controllers;
using MediCare.Web.Filters;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace MediCare.Tests;

public sealed class MedicalAttachmentAuthorizationTests
{
    [Fact]
    public void Upload_is_a_doctor_only_antiforgery_protected_post_with_request_size_limits()
    {
        var action = typeof(MedicalAttachmentsController).GetMethod(nameof(MedicalAttachmentsController.Upload))!;

        Assert.Equal(MediCareRoles.Doctor, action.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        var sizeLimit = action.GetCustomAttribute<RequestSizeLimitAttribute>();
        Assert.NotNull(sizeLimit);
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var values = sizeLimit!.GetType().GetProperties(all).Select(property => property.GetValue(sizeLimit))
            .Concat(sizeLimit.GetType().GetFields(all).Select(field => field.GetValue(sizeLimit)))
            .OfType<long>()
            .ToArray();
        Assert.Contains(MedicalAttachmentLimits.MaxRequestBytes, values);
        Assert.Equal(MedicalAttachmentLimits.MaxRequestBytes, action.GetCustomAttribute<RequestFormLimitsAttribute>()!.MultipartBodyLengthLimit);
    }

    [Fact]
    public void Download_is_open_to_the_doctor_and_the_patient_only_and_is_not_cacheable()
    {
        var action = typeof(MedicalAttachmentsController).GetMethod(nameof(MedicalAttachmentsController.Download))!;
        var roles = action.GetCustomAttribute<AuthorizeAttribute>()!.Roles!.Split(',');

        Assert.Contains(MediCareRoles.Doctor, roles);
        Assert.Contains(MediCareRoles.Patient, roles);
        Assert.DoesNotContain(MediCareRoles.Receptionist, roles);
        Assert.DoesNotContain(MediCareRoles.Admin, roles);
        Assert.Equal("Medical/Attachments/{attachmentId:guid}", action.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.NotNull(typeof(MedicalAttachmentsController).GetCustomAttribute<MedicalNoStoreAttribute>());
    }

    [Fact]
    public void The_upload_ceiling_is_the_default_file_limit_plus_form_overhead()
    {
        Assert.Equal(10L * 1024 * 1024, MedicalAttachmentLimits.DefaultMaxFileBytes);
        Assert.True(MedicalAttachmentLimits.MaxRequestBytes > MedicalAttachmentLimits.DefaultMaxFileBytes);
    }
}

public sealed class NotificationAuthorizationTests
{
    [Fact]
    public void Notifications_require_a_sign_in_but_no_particular_role()
    {
        var authorization = typeof(NotificationsController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.Null(authorization!.Roles);
    }

    [Theory]
    [InlineData(nameof(NotificationsController.Open))]
    [InlineData(nameof(NotificationsController.MarkAllRead))]
    public void State_changing_notification_actions_are_antiforgery_protected_posts(string actionName)
    {
        var action = typeof(NotificationsController).GetMethod(actionName)!;

        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}

/// <summary>PART C: a refused visit save re-renders the page with the typed values and the errors instead of losing them.</summary>
public sealed class VisitRedisplayTests
{
    private static readonly Guid QueueEntryId = Guid.NewGuid();
    private static readonly Guid DoctorUserId = Guid.NewGuid();

    [Fact]
    public async Task A_validation_failure_redisplays_the_visit_with_the_posted_values_and_the_error()
    {
        var controller = Create(new StubMedicalService());
        controller.ModelState.AddModelError("Visit.Diagnosis", "The Diagnosis field is required.");
        var posted = new VisitRecordForm { ChiefComplaint = "Cough for three days", Diagnosis = "", ClinicalNotes = "Afebrile" };

        var result = await controller.SaveVisit(QueueEntryId, posted, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Visit", view.ViewName);
        var model = Assert.IsType<VisitPageModel>(view.Model);
        Assert.Same(posted, model.Visit);
        Assert.Equal("Cough for three days", model.Visit.ChiefComplaint);
        Assert.True(controller.ModelState.ContainsKey("Visit.Diagnosis"));
    }

    [Fact]
    public async Task A_refused_save_redisplays_the_typed_values_with_the_service_message()
    {
        var controller = Create(new StubMedicalService(new MedicalActionResult(false, "The visit record is locked after its clinic day.")));
        var posted = new VisitRecordForm { ChiefComplaint = null, Diagnosis = "Bronchitis", ClinicalNotes = "Rest advised" };

        var result = await controller.SaveVisit(QueueEntryId, posted, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<VisitPageModel>(view.Model);
        Assert.Equal("Bronchitis", model.Visit.Diagnosis);
        Assert.Equal("Rest advised", model.Visit.ClinicalNotes);
        var error = controller.ModelState[string.Empty]!.Errors.Single();
        Assert.Equal("The visit record is locked after its clinic day.", error.ErrorMessage);
    }

    [Fact]
    public async Task A_successful_save_still_redirects_after_post()
    {
        var controller = Create(new StubMedicalService(new MedicalActionResult(true, "Visit record saved.", Guid.NewGuid())));

        var result = await controller.SaveVisit(QueueEntryId, new VisitRecordForm { Diagnosis = "Flu" }, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(MedicalRecordsController.Visit), redirect.ActionName);
    }

    private static MedicalRecordsController Create(IMedicalRecordService medical)
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, DoctorUserId.ToString()) }, "test");
        return new MedicalRecordsController(medical, new FixedClockForVisits())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } },
            TempData = new TempDataDictionary(new DefaultHttpContext(), new NoTempDataProvider())
        };
    }

    private sealed class FixedClockForVisits : IClinicClock
    {
        public DateTimeOffset UtcNow => new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
        public DateOnly Today => new(2026, 12, 1);
        public TimeOnly CurrentTime => new(11, 0);
        public DateTimeOffset ToUtc(DateOnly clinicDate, TimeOnly clinicTime) => new(clinicDate.ToDateTime(clinicTime), TimeSpan.Zero);
    }

    private sealed class NoTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }

    /// <summary>Only the two calls the visit save path makes are implemented; the rest are never reached in these tests.</summary>
    private sealed class StubMedicalService(MedicalActionResult? saveResult = null) : IMedicalRecordService
    {
        public Task<MedicalActionResult> CreateOrUpdateForVisitAsync(Guid queueEntryId, Guid doctorUserId, VisitRecordInput input, CancellationToken cancellationToken) =>
            Task.FromResult(saveResult ?? new MedicalActionResult(false, "unexpected"));

        public Task<VisitPageView?> GetVisitForDoctorAsync(Guid queueEntryId, Guid doctorUserId, CancellationToken cancellationToken) =>
            Task.FromResult<VisitPageView?>(new VisitPageView(queueEntryId, Guid.NewGuid(), "Test Patient", MediCare.Domain.Enums.QueueEntryStatus.InConsultation,
                new DateOnly(2026, 12, 1), null, CanCreate: true, CanEdit: false));

        public Task<MedicalActionResult> AddAddendumAsync(Guid recordId, Guid doctorUserId, string text, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<MedicalActionResult> SavePrescriptionAsync(Guid recordId, Guid doctorUserId, IReadOnlyList<PrescriptionItemInput> items, string? notes, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<MediCare.Application.Services.MedicalRecordView?> GetRecordAsync(Guid recordId, Guid viewerUserId, MediCare.Application.Services.MedicalViewerRole role, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PatientHistoryView?> GetPatientHistoryAsync(Guid patientId, Guid viewerUserId, MediCare.Application.Services.MedicalViewerRole role, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PatientHistoryView?> GetMyHistoryAsync(Guid patientUserId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<ActivePrescriptionView>> GetActivePrescriptionsAsync(Guid patientUserId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<DoctorVisitSummary>> GetDoctorVisitsForDateAsync(Guid doctorUserId, DateOnly date, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
