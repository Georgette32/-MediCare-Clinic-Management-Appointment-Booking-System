
using MediCare.Application.Common.Security;
using MediCare.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = Policies.AdminOnly)]
public class DashboardController : Controller
{
    private readonly IDashboardService _dashboard;
    private readonly IAnalyticsService _analytics;

    public DashboardController(
        IDashboardService dashboard,
        IAnalyticsService analytics)
    {
        _dashboard = dashboard;
        _analytics = analytics;
    }

    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var dashboard = await _dashboard
            .GetAdminDashboardAsync(cancellationToken);

        return View(dashboard);
    }

    public async Task<IActionResult> AppointmentAnalytics(
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken)
    {
        var result = await _analytics.GetAppointmentAnalyticsAsync(
            startDateUtc,
            endDateUtc,
            cancellationToken);

        return Ok(result);
    }

    public async Task<IActionResult> DoctorPerformance(
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken)
    {
        var result = await _analytics.GetDoctorPerformanceAsync(
            startDateUtc,
            endDateUtc,
            cancellationToken);

        return Ok(result);
    }

    public async Task<IActionResult> PeakHours(
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken)
    {
        var result = await _analytics.GetPeakHoursAsync(
            startDateUtc,
            endDateUtc,
            cancellationToken);

        return Ok(result);
    }

    public async Task<IActionResult> PopularSpecializations(
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken)
    {
        var result = await _analytics.GetPopularSpecializationsAsync(
            startDateUtc,
            endDateUtc,
            cancellationToken);

        return Ok(result);
    }
}
