using MediCare.Application.Abstractions;
using System.Security.Claims;
using MediCare.Application.Services;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

[Authorize(Roles = MediCareRoles.Doctor)]
public sealed class DoctorController(
    IWalkInAdmissionApprovalService admissionApprovalService,
    IQueueBoardService queueBoardService,
    IConsultationService consultationService,
    IClinicClock clinicClock,
    TimeZoneInfo clinicZone,
    IWorkingHourExtensionService extensionService,
    IDoctorScheduleService scheduleService,
    IMedicalRecordService medicalRecords,
    IAnalyticsService analytics) : Controller
{
    /// <summary>The signed-in doctor's own queueing statistics. The service resolves the doctor from the user, never from the form.</summary>
    [HttpGet]
    public async Task<IActionResult> Statistics(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        var today = clinicClock.Today;
        var toDate = to ?? today.AddDays(-1);
        var fromDate = from ?? toDate.AddDays(-29);
        var model = new MediCare.Web.Models.AnalyticsPageViewModel
        {
            FromDate = fromDate,
            ToDate = toDate,
            ClinicZone = clinicZone,
            IsOwnStatistics = true
        };

        try
        {
            var report = await analytics.GetOwnDoctorAnalyticsAsync(doctorUserId, fromDate, toDate, cancellationToken);
            return View("~/Views/Analytics/Doctor.cshtml", model with { Doctor = report });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException exception)
        {
            return View("~/Views/Analytics/Doctor.cshtml", model with { ErrorMessage = AnalyticsErrorText.From(exception) });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Schedule(CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        var doctorId = await scheduleService.GetOwnDoctorIdAsync(doctorUserId, cancellationToken);
        if (doctorId is null)
            return Forbid();

        try
        {
            var view = await scheduleService.GetScheduleAsync(new ScheduleActor(doctorUserId, IsAdmin: false), doctorId.Value, cancellationToken);
            return View(view);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet]
    public async Task<IActionResult> Queue(CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        try
        {
            var board = await queueBoardService.GetOwnDoctorQueueBoardAsync(doctorUserId, clinicClock.Today, cancellationToken);
            ViewBag.PendingExtensionCount = (await extensionService.GetPendingForDoctorUserAsync(doctorUserId, cancellationToken)).Count;
            ViewBag.Visits = await medicalRecords.GetDoctorVisitsForDateAsync(doctorUserId, clinicClock.Today, cancellationToken);
            return View(new QueueBoardPageViewModel
            {
                Board = board,
                ClinicDate = clinicClock.Today,
                ClinicZone = clinicZone
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet]
    public async Task<IActionResult> Extensions(DateOnly? date, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        var today = clinicClock.Today;
        try
        {
            var model = new DoctorExtensionsViewModel
            {
                Today = today,
                SelectedDate = date,
                Own = await extensionService.GetOwnAsync(doctorUserId, today.AddDays(-30), cancellationToken),
                Pending = await extensionService.GetPendingForDoctorUserAsync(doctorUserId, cancellationToken),
                Form = new WorkingHourExtensionRequestModel { Date = date ?? today }
            };
            model.WindowEnds = await extensionService.GetOwnWindowEndTimesAsync(doctorUserId, date ?? today, cancellationToken);
            return View(model);
        }
        catch (InvalidOperationException)
        {
            // The signed-in account is not linked to an active doctor profile.
            return Forbid();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateExtension(WorkingHourExtensionRequestModel model, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        if (!ModelState.IsValid || model.Date is not DateOnly date || model.OriginalEndTime is not TimeOnly original || model.ExtendedEndTime is not TimeOnly extended)
        {
            TempData["ErrorMessage"] = FirstModelError() ?? "Check the extension details and try again.";
            return RedirectToAction(nameof(Extensions), new { date = model.Date?.ToString("yyyy-MM-dd") });
        }

        var result = await extensionService.CreateOwnAsync(doctorUserId, date, original, extended, model.Reason, cancellationToken);
        SetResult(result);
        return RedirectToAction(nameof(Extensions), new { date = date.ToString("yyyy-MM-dd") });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DecideExtension(DoctorExtensionDecisionModel model, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        var result = await extensionService.DecideAsync(model.ExtensionId, doctorUserId, model.Approve, model.Notes, cancellationToken);
        SetResult(result);
        return RedirectToAction(nameof(Extensions));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelExtension(Guid extensionId, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        var result = await extensionService.CancelAsync(extensionId, doctorUserId, isDoctorOwner: true, cancellationToken);
        SetResult(result);
        return RedirectToAction(nameof(Extensions));
    }

    private void SetResult(WorkingHourExtensionResult result)
    {
        if (result.Succeeded)
            TempData["StatusMessage"] = result.Message;
        else
            TempData["ErrorMessage"] = result.Message;
    }

    private string? FirstModelError() =>
        ModelState.Values.SelectMany(entry => entry.Errors).Select(error => error.ErrorMessage).FirstOrDefault();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartConsultation(Guid queueEntryId, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        var result = await consultationService.StartConsultationAsync(queueEntryId, doctorUserId, cancellationToken);
        SetResultMessage(result);
        return RedirectToAction(nameof(Queue));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteConsultation(Guid queueEntryId, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        var result = await consultationService.CompleteConsultationAsync(queueEntryId, doctorUserId, cancellationToken);
        SetResultMessage(result);
        return RedirectToAction(nameof(Queue));
    }

    private void SetResultMessage(ConsultationActionResult result)
    {
        if (result.Succeeded)
            TempData["StatusMessage"] = result.Message;
        else
            TempData["ErrorMessage"] = result.Message;
    }

    [HttpGet]
    public async Task<IActionResult> WalkInAdmissions(CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        var pending = await admissionApprovalService.GetPendingForDoctorAsync(doctorUserId, cancellationToken);
        return View(new WalkInAdmissionsViewModel(pending));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DecideWalkInAdmission(WalkInAdmissionDecisionViewModel model, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var doctorUserId))
            return Forbid();

        try
        {
            TempData["StatusMessage"] = await admissionApprovalService.DecideAsync(
                model.ApprovalId, doctorUserId, model.Approve, model.Notes, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            TempData["StatusMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(WalkInAdmissions));
    }

    private bool TryGetApplicationUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
