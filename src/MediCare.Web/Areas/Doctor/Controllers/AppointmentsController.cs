using MediCare.Application.Common.Security;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Web.Helpers;
using MediCare.Web.ViewModels.Doctor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Doctor.Controllers;

[Area("Doctor")]
[Authorize(Policy = Policies.ApprovedDoctor)]
public sealed class AppointmentsController(
    IAppointmentRepository appointments,
    ICurrentUserService currentUser,
    IClinicClock clock) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Challenge();
        }

        var doctor = await appointments.GetDoctorProfileByUserIdAsync(userId.Value, cancellationToken);
        if (doctor is null)
        {
            return NotFound();
        }

        var items = await DoctorAppointmentLoader.LoadAsync(
            appointments, doctor.Id, clock.Today, days: 30, cancellationToken);

        return View(new DoctorAppointmentsViewModel { Appointments = items });
    }
}
