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
    public async Task<IActionResult> Details(Guid id, DateOnly? date, DateOnly? month, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var selected = date ?? today;
        var profile = await directory.GetProfileAsync(id, selected, cancellationToken);
        if (profile is null)
            return NotFound();

        // Without a chosen date, open on the doctor's next working day instead of a day they do not work.
        if (date is null && !DoctorCalendarMonth.IsOpen(today, today, profile.WeeklyHours, profile.UpcomingHolidays)
            && DoctorCalendarMonth.FirstOpenDay(today, today, profile.WeeklyHours, profile.UpcomingHolidays) is { } next)
        {
            selected = next;
            profile = await directory.GetProfileAsync(id, selected, cancellationToken) ?? profile;
        }

        return View(new DoctorDetailsPageModel
        {
            Profile = profile,
            Today = today,
            SelectedIsOpen = DoctorCalendarMonth.IsOpen(selected, today, profile.WeeklyHours, profile.UpcomingHolidays),
            Calendar = DoctorCalendarMonth.Build(month ?? selected, today, selected, profile.WeeklyHours, profile.UpcomingHolidays)
        });
    }
}
