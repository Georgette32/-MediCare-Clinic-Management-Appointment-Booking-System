using System.Security.Claims;
using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using MediCare.Application.Services;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MediCare.Web.Controllers;

[Authorize(Roles = MediCareRoles.Admin)]
public sealed class AdminController(
    IDoctorScheduleService schedule,
    IStaffAccountService staff,
    ISpecializationAdministrationService specializations,
    IClinicSettingsService clinicSettings,
    IConfiguration configuration,
    IOptions<WalkInAdmissionApprovalOptions> walkInOptions) : Controller
{
    [HttpGet("Admin/DoctorSchedule/{doctorId:guid?}")]
    public async Task<IActionResult> DoctorSchedule(Guid? doctorId, CancellationToken cancellationToken)
    {
        var doctors = (await staff.ListAsync(cancellationToken))
            .Where(item => item.Role == MediCareRoles.Doctor && item.DoctorId is not null)
            .ToArray();
        var model = new AdminDoctorSchedulePageModel { Doctors = doctors };

        if (doctorId is Guid selected)
        {
            try
            {
                model.Schedule = await schedule.GetScheduleAsync(CurrentActor(), selected, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                return NotFound();
            }
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Specializations(CancellationToken cancellationToken) =>
        View(new SpecializationsPageModel { Items = await specializations.ListAsync(cancellationToken) });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSpecialization(SpecializationFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return ReturnSpecializationsWithErrors();

        var result = await specializations.CreateAsync(model.Name, model.Description, cancellationToken);
        SetResult(result);
        return RedirectToAction(nameof(Specializations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSpecialization(Guid specializationId, SpecializationFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return ReturnSpecializationsWithErrors();

        var result = await specializations.UpdateAsync(specializationId, model.Name, model.Description, cancellationToken);
        SetResult(result);
        return RedirectToAction(nameof(Specializations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSpecialization(Guid specializationId, CancellationToken cancellationToken)
    {
        var result = await specializations.DeleteAsync(specializationId, cancellationToken);
        SetResult(result);
        return RedirectToAction(nameof(Specializations));
    }

    [HttpGet]
    public async Task<IActionResult> Staff(CancellationToken cancellationToken) =>
        View(await staff.ListAsync(cancellationToken));

    /// <summary>Confirm step before deactivating a doctor: shows how many appointments and waiting entries would be cancelled.</summary>
    [HttpGet]
    public async Task<IActionResult> ConfirmDeactivateStaff(Guid userId, CancellationToken cancellationToken)
    {
        var row = (await staff.ListAsync(cancellationToken)).FirstOrDefault(item => item.UserId == userId);
        if (row is null || row.Role != MediCareRoles.Doctor || !row.IsActive)
            return NotFound();

        var preview = await staff.PreviewDoctorDeactivationAsync(userId, cancellationToken);
        return View(new StaffDeactivationViewModel { UserId = userId, FullName = row.FullName, Preview = preview });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStaffActive(Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
            return Forbid();

        var result = await staff.SetActiveAsync(userId, isActive, adminId, cancellationToken);
        SetResult(result);
        return RedirectToAction(nameof(Staff));
    }

    [HttpGet]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken)
    {
        var settings = await clinicSettings.GetAsync(cancellationToken);
        return View(new ClinicSettingsPageModel
        {
            Settings = settings,
            ClinicTimeZoneId = configuration["Clinic:TimeZoneId"] ?? "Not configured",
            WalkInPendingDurationMinutes = walkInOptions.Value.PendingDurationMinutes
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(ClinicSettingsFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(entry => entry.Errors).Select(error => error.ErrorMessage).FirstOrDefault();
            return RedirectToAction(nameof(Settings));
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
            return Forbid();

        var result = await clinicSettings.SetMinimumSampleSizeAsync(model.MinimumConsultationSampleSize, adminId, cancellationToken);
        SetResult(result);
        return RedirectToAction(nameof(Settings));
    }

    private ScheduleActor CurrentActor()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;
        return new ScheduleActor(userId, IsAdmin: true);
    }

    private IActionResult ReturnSpecializationsWithErrors()
    {
        TempData["ErrorMessage"] = ModelState.Values.SelectMany(entry => entry.Errors).Select(error => error.ErrorMessage).FirstOrDefault()
                                   ?? "Check the specialization details and try again.";
        return RedirectToAction(nameof(Specializations));
    }

    private void SetResult(ManagementResult result)
    {
        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Message;
    }
}
