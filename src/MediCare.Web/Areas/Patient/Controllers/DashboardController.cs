using MediCare.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Areas.Patient.Controllers;

[Area("Patient")]
[Authorize(Policy = Policies.PatientOnly)]
public class DashboardController : Controller
{
    public IActionResult Index() => View();
}
