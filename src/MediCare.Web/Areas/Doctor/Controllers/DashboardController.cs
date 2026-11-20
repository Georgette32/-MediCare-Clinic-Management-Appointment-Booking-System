using MediCare.Application.Common.Security;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Services;
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

    public DashboardController(IDoctorManagementService doctors, ICurrentUserService currentUser)
    {
        _doctors = doctors;
        _currentUser = currentUser;
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

        return View(new DoctorDashboardViewModel
        {
            Doctor = result.Value,
            FullName = result.Value.FullName
        });
    }
}
