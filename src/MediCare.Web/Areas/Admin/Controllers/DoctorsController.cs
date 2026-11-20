using MediCare.Application.Common;
using MediCare.Application.Common.Security;
using MediCare.Application.DTOs.Doctors;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Services;
using MediCare.Domain.Enums;
using MediCare.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = Policies.AdminOnly)]
public class DoctorsController : Controller
{
    private readonly IDoctorManagementService _doctors;
    private readonly ISpecializationService _specializations;
    private readonly ICurrentUserService _currentUser;

    public DoctorsController(
        IDoctorManagementService doctors,
        ISpecializationService specializations,
        ICurrentUserService currentUser)
    {
        _doctors = doctors;
        _specializations = specializations;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(DoctorQuery query, CancellationToken cancellationToken)
    {
        var page = await _doctors.GetDoctorsAsync(query, cancellationToken);
        var pendingCount = await _doctors.GetDoctorsAsync(
            new DoctorQuery { ApprovalStatus = DoctorApprovalStatus.Pending, PageSize = 1 },
            cancellationToken);

        var model = new DoctorListViewModel
        {
            Search = query.Search,
            ApprovalStatus = query.ApprovalStatus,
            IsActive = query.IsActive,
            Page = page.Page,
            PageSize = page.PageSize,
            Doctors = new PagedResultView<DoctorListItemDto>
            {
                Items = page.Items,
                Page = page.Page,
                PageSize = page.PageSize,
                TotalCount = page.TotalCount
            }
        };

        ViewBag.PendingCount = pendingCount.TotalCount;
        return View(model);
    }

    public async Task<IActionResult> Pending(CancellationToken cancellationToken)
    {
        var pending = await _doctors.GetPendingDoctorsAsync(cancellationToken);
        ViewBag.PendingCount = pending.Count;
        return View(pending);
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var result = await _doctors.GetDoctorAsync(id, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return NotFound();
        }

        return View(result.Value);
    }

    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var result = await _doctors.GetDoctorAsync(id, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return NotFound();
        }

        var model = await ToEditViewModelAsync(result.Value, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(DoctorEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateSpecializationsAsync(model, cancellationToken);
            return View(model);
        }

        var result = await _doctors.UpdateAsync(
            _currentUser.UserId ?? Guid.Empty,
            new UpdateDoctorRequest
            {
                DoctorId = model.DoctorId,
                FullName = model.FullName,
                PhoneNumber = model.PhoneNumber,
                SpecializationId = model.SpecializationId,
                LicenseNumber = model.LicenseNumber,
                ConsultationFee = model.ConsultationFee,
                YearsOfExperience = model.YearsOfExperience,
                Bio = model.Bio
            },
            cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result);
            await PopulateSpecializationsAsync(model, cancellationToken);
            return View(model);
        }

        TempData["Success"] = "Doctor account information was updated.";
        return RedirectToAction(nameof(Details), new { id = model.DoctorId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        var result = await _doctors.ApproveAsync(_currentUser.UserId ?? Guid.Empty, id, cancellationToken);
        SetResultMessage(result, "Doctor application approved.", "Could not approve the doctor.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(RejectDoctorViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "A rejection reason is required.";
            return RedirectToAction(nameof(Details), new { id = model.DoctorId });
        }

        var result = await _doctors.RejectAsync(
            _currentUser.UserId ?? Guid.Empty,
            model.DoctorId,
            model.Reason,
            cancellationToken);

        SetResultMessage(result, "Doctor application rejected.", "Could not reject the doctor.");
        return RedirectToAction(nameof(Details), new { id = model.DoctorId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Suspend(Guid id, string reason, CancellationToken cancellationToken)
    {
        var result = await _doctors.SuspendAsync(
            _currentUser.UserId ?? Guid.Empty,
            id,
            reason,
            cancellationToken);

        SetResultMessage(result, "Doctor access suspended.", "Could not suspend the doctor.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _doctors.SetAccountActiveAsync(_currentUser.UserId ?? Guid.Empty, id, true, cancellationToken);
        SetResultMessage(result, "Doctor account activated.", "Could not activate the doctor account.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _doctors.SetAccountActiveAsync(_currentUser.UserId ?? Guid.Empty, id, false, cancellationToken);
        SetResultMessage(result, "Doctor account deactivated.", "Could not deactivate the doctor account.");
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<DoctorEditViewModel> ToEditViewModelAsync(DoctorDetailsDto doctor, CancellationToken cancellationToken)
    {
        var model = new DoctorEditViewModel
        {
            DoctorId = doctor.Id,
            Email = doctor.Email,
            CurrentApprovalStatus = doctor.ApprovalStatus.ToString(),
            FullName = doctor.FullName,
            PhoneNumber = doctor.PhoneNumber ?? string.Empty,
            SpecializationId = doctor.SpecializationId,
            LicenseNumber = doctor.LicenseNumber,
            ConsultationFee = doctor.ConsultationFee,
            YearsOfExperience = doctor.YearsOfExperience,
            Bio = doctor.Bio
        };

        await PopulateSpecializationsAsync(model, cancellationToken);
        return model;
    }

    private async Task PopulateSpecializationsAsync(DoctorEditViewModel model, CancellationToken cancellationToken)
    {
        model.Specializations = await _specializations.GetAsync(false, cancellationToken);
    }

    private void AddErrors(Result result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }

    private void SetResultMessage(Result result, string success, string failure)
    {
        if (result.Succeeded)
        {
            TempData["Success"] = success;
        }
        else
        {
            TempData["Error"] = string.Join(" ", result.Errors.DefaultIfEmpty(failure));
        }
    }
}
