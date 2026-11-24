using MediCare.Application.Services;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

[Authorize(Roles = MediCareRoles.Admin)]
public sealed class AdminAccountsController(IAccountProvisioningService accounts) : Controller
{
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> Doctor(CancellationToken cancellationToken)
    {
        var model = new DoctorAccountViewModel();
        await LoadSpecializationsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Doctor(DoctorAccountViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadSpecializationsAsync(model, cancellationToken);
            return View(model);
        }

        var result = await accounts.CreateDoctorAsync(new CreateDoctorAccountRequest(
            model.Email, model.FullName, model.SpecializationId!.Value, model.AppointmentDurationMinutes!.Value), cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            await LoadSpecializationsAsync(model, cancellationToken);
            return View(model);
        }

        SetNoStore();
        return View("~/Views/Shared/AccountCreated.cshtml", new AccountCreatedViewModel
        {
            FullName = model.FullName,
            UserName = model.Email,
            Role = MediCareRoles.Doctor,
            TemporaryPassword = result.TemporaryPassword!
        });
    }

    [HttpGet]
    public IActionResult Receptionist() => View(new StaffAccountViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receptionist(StaffAccountViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await accounts.CreateReceptionistAsync(new CreateStaffAccountRequest(model.Email, model.FullName), cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return View(model);
        }

        SetNoStore();
        return View("~/Views/Shared/AccountCreated.cshtml", new AccountCreatedViewModel
        {
            FullName = model.FullName,
            UserName = model.Email,
            Role = MediCareRoles.Receptionist,
            TemporaryPassword = result.TemporaryPassword!
        });
    }

    private async Task LoadSpecializationsAsync(DoctorAccountViewModel model, CancellationToken cancellationToken) =>
        model.Specializations = await accounts.GetSpecializationsAsync(cancellationToken);

    private void AddErrors(IEnumerable<string> errors)
    {
        foreach (var error in errors)
            ModelState.AddModelError(string.Empty, error);
    }

    private void SetNoStore() => Response.Headers.CacheControl = "no-store, no-cache";
}
