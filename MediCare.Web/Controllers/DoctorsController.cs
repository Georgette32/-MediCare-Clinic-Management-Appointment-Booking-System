using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

/// <summary>Doctor discovery for every signed-in role. Booking itself is not part of this controller.</summary>
[Authorize]
public sealed class DoctorsController(
    IDoctorDirectoryService directory,
    ISpecializationAdministrationService specializations,
    IClinicClock clock) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(Guid? specializationId, string? name, DateOnly? date, CancellationToken cancellationToken)
    {
        var model = new DoctorSearchPageModel
        {
            Today = clock.Today,
            SpecializationId = specializationId,
            Name = name,
            Date = date,
            Specializations = await specializations.ListAsync(cancellationToken),
            Doctors = await directory.SearchAsync(specializationId, name, date, cancellationToken)
        };
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, DateOnly? date, CancellationToken cancellationToken)
    {
        var selected = date ?? clock.Today;
        var profile = await directory.GetProfileAsync(id, selected, cancellationToken);
        if (profile is null)
            return NotFound();

        ViewBag.Today = clock.Today;
        return View(profile);
    }
}
