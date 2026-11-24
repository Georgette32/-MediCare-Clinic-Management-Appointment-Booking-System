using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

/// <summary>
/// Queue analytics. Admin sees the full clinic (historical). Receptionist and Admin see the live operations board.
/// A doctor's own statistics are served by DoctorController.Statistics, which checks ownership on the server.
/// </summary>
[Authorize(Roles = MediCareRoles.Admin + "," + MediCareRoles.Receptionist)]
public sealed class AnalyticsController(
    IAnalyticsService analytics,
    IClinicClock clinicClock,
    TimeZoneInfo clinicZone) : Controller
{
    [Authorize(Roles = MediCareRoles.Admin)]
    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var (defaultFrom, defaultTo) = AnalyticsPageViewModel.DefaultHistoricalRange(clinicClock.Today);
        var fromDate = from ?? defaultFrom;
        var toDate = to ?? defaultTo;
        var model = new AnalyticsPageViewModel { FromDate = fromDate, ToDate = toDate, ClinicZone = clinicZone };

        try
        {
            var report = await analytics.GetClinicAnalyticsAsync(fromDate, toDate, cancellationToken);
            return View(model with { Clinic = report });
        }
        catch (ArgumentException exception)
        {
            return View(model with { ErrorMessage = exception.Message });
        }
    }

    [Authorize(Roles = MediCareRoles.Admin)]
    [HttpGet]
    public async Task<IActionResult> Doctor(Guid id, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var (defaultFrom, defaultTo) = AnalyticsPageViewModel.DefaultHistoricalRange(clinicClock.Today);
        var fromDate = from ?? defaultFrom;
        var toDate = to ?? defaultTo;
        var model = new AnalyticsPageViewModel { FromDate = fromDate, ToDate = toDate, ClinicZone = clinicZone, SelectedDoctorId = id };

        try
        {
            var report = await analytics.GetDoctorAnalyticsAsync(id, fromDate, toDate, cancellationToken);
            if (report is null)
                return NotFound();

            return View("Doctor", model with { Doctor = report });
        }
        catch (ArgumentException exception)
        {
            return View("Doctor", model with { ErrorMessage = exception.Message });
        }
    }

    /// <summary>Refreshable board. Each refresh is a new GET request; the page does not poll.</summary>
    [Authorize(Roles = MediCareRoles.Receptionist + "," + MediCareRoles.Admin)]
    [HttpGet]
    public async Task<IActionResult> Live(Guid? doctorId, CancellationToken cancellationToken)
    {
        var report = await analytics.GetLiveAsync(null, cancellationToken);
        var rows = doctorId is null ? report.Rows : report.Rows.Where(row => row.DoctorId == doctorId.Value).ToArray();

        return View(new AnalyticsPageViewModel
        {
            FromDate = report.Today,
            ToDate = report.Today,
            Live = report with { Rows = rows },
            SelectedDoctorId = doctorId,
            Doctors = report.Rows.Select(row => new DoctorChoice(row.DoctorId, row.DoctorName)).ToArray(),
            ClinicZone = clinicZone
        });
    }
}
