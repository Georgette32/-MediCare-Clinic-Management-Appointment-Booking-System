using MediCare.Application.Common;
using MediCare.Application.Common.Security;
using MediCare.Application.DTOs.Specializations;
using MediCare.Application.Interfaces.Services;
using MediCare.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = Policies.AdminOnly)]
public class SpecializationsController : Controller
{
    private readonly ISpecializationService _specializations;

    public SpecializationsController(ISpecializationService specializations)
    {
        _specializations = specializations;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var items = await _specializations.GetAsync(true, cancellationToken);
        return View(new SpecializationListViewModel { Specializations = items });
    }

    [HttpGet]
    public IActionResult Create() => View(new SpecializationFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SpecializationFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _specializations.CreateAsync(new CreateSpecializationRequest
        {
            Name = model.Name,
            Description = model.Description
        }, cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["Success"] = "Specialization created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var result = await _specializations.GetByIdAsync(id, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return NotFound();
        }

        return View(new SpecializationFormViewModel
        {
            Id = result.Value.Id,
            Name = result.Value.Name,
            Description = result.Value.Description
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(SpecializationFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _specializations.UpdateAsync(new UpdateSpecializationRequest
        {
            Id = model.Id,
            Name = model.Name,
            Description = model.Description
        }, cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["Success"] = "Specialization updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(Guid id, CancellationToken cancellationToken)
    {
        var current = await _specializations.GetByIdAsync(id, cancellationToken);
        if (!current.Succeeded || current.Value is null)
        {
            return NotFound();
        }

        var result = await _specializations.SetActiveAsync(id, !current.Value.IsActive, cancellationToken);
        SetResultMessage(result, "Specialization status updated.", "Could not update the specialization.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _specializations.DeleteAsync(id, cancellationToken);
        SetResultMessage(result, "Specialization deleted.", "Could not delete the specialization.");
        return RedirectToAction(nameof(Index));
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
