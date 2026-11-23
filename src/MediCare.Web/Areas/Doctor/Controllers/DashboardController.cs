using MediCare.Application.Common.Security;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Web.Helpers;
using MediCare.Web.ViewModels.Doctor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Doctor.Controllers;

[Area("Doctor")]
[Authorize(Policy = Policies.ApprovedDoctor)]
public class DashboardController : Controller
{
    private readonly IDoctorManagementService _doctors;
    private readonly ICurrentUserService _currentUser;
    private readonly IAppointmentRepository _appointments;
    private readonly IClinicClock _clock;

    public DashboardController(
        IDoctorManagementService doctors,
        ICurrentUserService currentUser,
        IAppointmentRepository appointments,
        IClinicClock clock)
    {
        _doctors = doctors;
        _currentUser = currentUser;
        _appointments = appointments;
        _clock = clock;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return Challenge();
        }

        var result = await _doctors.GetDoctorByUserIdAsync(userId.Value, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return NotFound();
        }

        var upcomingAppointments = await DoctorAppointmentLoader.LoadAsync(
            _appointments, result.Value.Id, cancellationToken);

        return View(new DoctorDashboardViewModel
        {
            Doctor = result.Value,
            FullName = result.Value.FullName,
            UpcomingAppointments = upcomingAppointments
                .Where(item => item.Date >= _clock.Today)
                .Take(5)
                .ToArray()
        });
    }
}
