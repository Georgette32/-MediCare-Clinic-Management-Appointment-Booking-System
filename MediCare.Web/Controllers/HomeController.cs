using System.Diagnostics;
using System.Security.Claims;
using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using MediCare.Web.Models;

namespace MediCare.Web.Controllers;

public class HomeController : Controller
{
    /// <summary>Home leads with the one thing each role came to check. Every read goes through the same service its own page uses.</summary>
    public async Task<IActionResult> Index(
        [FromServices] AppointmentService appointments,
        [FromServices] IAppointmentQueryService queries,
        [FromServices] IQueueBoardService queueBoards,
        [FromServices] IClinicClock clock,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return View();

        if (User.IsInRole(MediCareRoles.Patient))
        {
            if (await queries.GetPatientIdForUserAsync(userId, cancellationToken) is Guid patientId)
            {
                var board = await queries.GetPatientBoardAsync(patientId, cancellationToken);
                var next = board.Upcoming.FirstOrDefault(row => row.Record.Status is AppointmentStatus.Confirmed or AppointmentStatus.Pending)?.Record;
                ViewBag.Now = next is null
                    ? "You have no upcoming visits."
                    : $"Next visit: {next.Date:dddd d MMMM} at {next.StartTime:HH:mm} with {next.DoctorName}{(next.Status == AppointmentStatus.Pending ? ", waiting for confirmation" : "")}.";
            }
        }
        else if (User.IsInRole(MediCareRoles.Doctor))
        {
            try
            {
                var board = await queueBoards.GetOwnDoctorQueueBoardAsync(userId, clock.Today, cancellationToken);
                var waiting = board.Scheduled.Count(item => item.State == ScheduledBoardState.Waiting)
                    + board.WalkIns.Count(item => item.Status == QueueEntryStatus.Waiting);
                var toArrive = board.Scheduled.Count(item => item.State == ScheduledBoardState.NotArrived);
                ViewBag.Now = $"{waiting} waiting now, {toArrive} still to arrive today.";
            }
            catch (UnauthorizedAccessException)
            {
                // An account without an active doctor profile still gets the plain Home page.
            }
        }
        else if (User.IsInRole(MediCareRoles.Receptionist) || User.IsInRole(MediCareRoles.Admin))
        {
            var pending = (await appointments.GetPendingAsync(cancellationToken)).Count;
            ViewBag.PendingRequests = pending;
            ViewBag.Now = pending == 0
                ? "No appointment requests are waiting."
                : $"{pending} appointment request{(pending == 1 ? " is" : "s are")} waiting for an answer.";
        }

        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
