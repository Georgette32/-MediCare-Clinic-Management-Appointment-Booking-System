using System.Security.Claims;
using MediCare.Application;
using MediCare.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

[Authorize]
public sealed class AppointmentsController(IAppointmentService appointments) : Controller
{
    private string UserKey => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();

    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, AppointmentStatus? status, CancellationToken ct)
    {
        await appointments.ExpireOffersAsync(ct);
        ViewData["Offers"] = await appointments.GetPatientOffersAsync(UserKey, ct);
        ViewData["From"] = from?.ToString("yyyy-MM-dd");
        ViewData["To"] = to?.ToString("yyyy-MM-dd");
        ViewData["Status"] = status?.ToString();
        return View(await appointments.GetPatientAppointmentsAsync(UserKey, from, to, status, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Doctors(string? search, CancellationToken ct) => View(await appointments.SearchDoctorsAsync(search, ct));

    [HttpGet]
    public async Task<IActionResult> Notifications(CancellationToken ct) => View(await appointments.GetPatientNotificationsAsync(UserKey, ct));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkNotificationRead(long id, CancellationToken ct)
    {
        await appointments.MarkNotificationReadAsync(id, UserKey, ct);
        return RedirectToAction(nameof(Notifications));
    }

    [HttpGet]
    public IActionResult Book(string? doctorId = null) => View(new BookAppointmentInput { DoctorId = doctorId ?? string.Empty });

    [HttpGet]
    public async Task<IActionResult> AvailableSlots(string doctorId, DateOnly date, CancellationToken ct) =>
        Json(await appointments.GetAvailableSlotsAsync(doctorId, date, ct));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Book(BookAppointmentInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(input);
        try { await appointments.BookAsync(UserKey, input.DoctorId, input.Start, ct); return RedirectToAction(nameof(Index)); }
        catch (InvalidOperationException ex) { ModelState.AddModelError(string.Empty, ex.Message); return View(input); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(long id, string? reason, CancellationToken ct)
    {
        try { await appointments.CancelAsync(id, UserKey, reason, ct); TempData["Message"] = "Appointment cancelled."; }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reschedule(long id, DateTimeOffset start, CancellationToken ct)
    {
        try { await appointments.RescheduleAsync(id, UserKey, start, ct); TempData["Message"] = "Appointment rescheduled."; }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckIn(long id, CancellationToken ct)
    {
        try { await appointments.CheckInAsync(id, UserKey, DateTimeOffset.UtcNow, ct); TempData["Message"] = "Check-in recorded."; }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Doctor,Admin")]
    public async Task<IActionResult> Daily(DateOnly? date, CancellationToken ct) => View(await appointments.GetDoctorAppointmentsAsync(UserKey, date ?? DateOnly.FromDateTime(DateTime.UtcNow), ct));

    [Authorize(Roles = "Doctor,Admin")]
    public IActionResult Calendar() => View();

    [Authorize(Roles = "Doctor,Admin"), HttpGet]
    public async Task<IActionResult> CalendarEvents(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var events = await appointments.GetScheduleEventsAsync(UserKey, start, end, ct);
        return Json(events.Select(e => new { id = e.Id, title = $"{e.PatientId} · {e.Status}", start = e.Start, end = e.End, extendedProps = new { status = e.Status } }));
    }

    [Authorize(Roles = "Doctor,Admin"), HttpGet]
    public async Task<IActionResult> ScheduleSettings(CancellationToken ct)
    {
        var settings = await appointments.GetDoctorSettingsAsync(UserKey, ct);
        ViewData["Settings"] = settings;
        ViewData["Hours"] = await appointments.GetDoctorWorkingHoursAsync(UserKey, ct);
        ViewData["Breaks"] = await appointments.GetDoctorBreaksAsync(UserKey, ct);
        ViewData["Leaves"] = await appointments.GetDoctorLeavesAsync(UserKey, ct);
        return View(settings is null ? new ScheduleSettingsInput() : new ScheduleSettingsInput { PublicDisplayName = settings.PublicDisplayName, Specialization = settings.Specialization, ConsultationFee = settings.ConsultationFee, DurationMinutes = settings.DefaultConsultationDurationMinutes, GraceMultiplier = settings.GracePeriodMultiplier, SafetyBufferMultiplier = settings.SafetyBufferMultiplier, MinimumSamples = settings.MinimumHistorySamples, BookedCapacityReservation = settings.BookedCapacityReservation });
    }

    [Authorize(Roles = "Doctor,Admin"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveScheduleSettings(ScheduleSettingsInput input, CancellationToken ct)
    {
        try
        {
            await appointments.SaveDoctorSettingsAsync(new DoctorSchedulingSettings { DoctorId = UserKey, PublicDisplayName = input.PublicDisplayName, Specialization = input.Specialization, ConsultationFee = input.ConsultationFee, DefaultConsultationDurationMinutes = input.DurationMinutes, GracePeriodMultiplier = input.GraceMultiplier, SafetyBufferMultiplier = input.SafetyBufferMultiplier, MinimumHistorySamples = input.MinimumSamples, BookedCapacityReservation = input.BookedCapacityReservation }, ct);
            TempData["Message"] = "Scheduling settings saved.";
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(ScheduleSettings));
    }

    [Authorize(Roles = "Doctor,Admin"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveWorkingHour(DayOfWeek day, TimeOnly start, TimeOnly end, CancellationToken ct)
    {
        try { await appointments.SaveWorkingHourAsync(UserKey, day, start, end, ct); TempData["Message"] = "Working hours saved."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(ScheduleSettings));
    }

    [Authorize(Roles = "Doctor,Admin"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBreak(DateTimeOffset startsAt, DateTimeOffset endsAt, bool recurring, DayOfWeek? day, CancellationToken ct)
    {
        try { await appointments.AddBreakAsync(new DoctorBreak { DoctorId = UserKey, StartsAt = startsAt.ToUniversalTime(), EndsAt = endsAt.ToUniversalTime(), IsRecurring = recurring, RecurringDay = day }, ct); TempData["Message"] = "Break saved."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(ScheduleSettings));
    }

    [Authorize(Roles = "Doctor,Admin"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddLeave(DateTimeOffset startsAt, DateTimeOffset endsAt, string? reason, CancellationToken ct)
    {
        try { await appointments.AddLeaveAsync(new DoctorLeave { DoctorId = UserKey, StartsAt = startsAt.ToUniversalTime(), EndsAt = endsAt.ToUniversalTime(), Reason = reason ?? string.Empty }, ct); TempData["Message"] = "Leave period saved."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(ScheduleSettings));
    }

    [Authorize(Roles = "Doctor,Admin")]
    public async Task<IActionResult> Queue(CancellationToken ct) => View(await appointments.GetQueueAsync(UserKey, ct));

    [Authorize(Roles = "Doctor,Admin"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateQueueSession(CancellationToken ct)
    {
        try { await appointments.CreateQueueSessionAsync(UserKey, ct); TempData["Message"] = "Queue session prepared. Start it when you are ready."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Queue));
    }

    [Authorize(Roles = "Doctor,Admin"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> StartQueueSession(long id, CancellationToken ct)
    {
        try { await appointments.StartQueueSessionAsync(id, UserKey, ct); TempData["Message"] = "Queue session started."; }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Queue));
    }

    [Authorize(Roles = "Doctor,Admin"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseQueueSession(long id, CancellationToken ct)
    {
        try { await appointments.CloseQueueSessionAsync(id, UserKey, ct); TempData["Message"] = "Queue session closed."; }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Queue));
    }

    [Authorize(Roles = "Doctor,Admin"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CallNext(CancellationToken ct)
    {
        try { await appointments.CallNextAsync(UserKey, ct); TempData["Message"] = "Next patient called."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Queue));
    }

    [Authorize(Roles = "Doctor,Admin"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(long id, CancellationToken ct)
    {
        try { await appointments.StartConsultationAsync(id, UserKey, ct); TempData["Message"] = "Consultation started."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Queue));
    }

    [Authorize(Roles = "Doctor,Admin"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(long id, CancellationToken ct)
    {
        try { await appointments.CompleteConsultationAsync(id, UserKey, ct); TempData["Message"] = "Consultation completed."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Queue));
    }

    [Authorize(Roles = "Doctor,Admin"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> WalkIn(string patientId, CancellationToken ct)
    {
        await appointments.RegisterWalkInAsync(UserKey, patientId, DateTimeOffset.UtcNow, ct);
        return RedirectToAction(nameof(Queue));
    }

    [Authorize(Roles = "Doctor,Admin"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DecideWalkIn(long id, bool approve, bool defer = false, CancellationToken ct = default)
    {
        try { await appointments.ApproveWalkInAsync(id, UserKey, approve, defer, ct); TempData["Message"] = "Walk-in decision recorded."; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Queue));
    }

    [HttpGet]
    public IActionResult WaitingList() => View(new WaitingListInput());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> WaitingList(WaitingListInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(input);
        try
        {
            await appointments.JoinWaitingListAsync(new WaitingListEntry { PatientId = UserKey, PreferredDoctorId = input.DoctorId, PreferredDateFrom = input.DateFrom, PreferredDateTo = input.DateTo, PreferredTimeFrom = input.TimeFrom, PreferredTimeTo = input.TimeTo }, ct);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(input);
        }
        TempData["Message"] = "You joined the waiting list.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptOffer(long id, CancellationToken ct)
    { try { await appointments.AcceptOfferAsync(id, UserKey, ct); TempData["Message"] = "Offer accepted and appointment confirmed."; } catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; } return RedirectToAction(nameof(Index)); }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeclineOffer(long id, CancellationToken ct)
    { try { await appointments.DeclineOfferAsync(id, UserKey, ct); TempData["Message"] = "Offer declined."; } catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; } return RedirectToAction(nameof(Index)); }
}

public sealed class BookAppointmentInput
{
    [System.ComponentModel.DataAnnotations.Required] public string DoctorId { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DataType(System.ComponentModel.DataAnnotations.DataType.DateTime)] public DateTimeOffset Start { get; set; }
}

public sealed class ScheduleSettingsInput
{
    [System.ComponentModel.DataAnnotations.StringLength(100)] public string? PublicDisplayName { get; set; }
    [System.ComponentModel.DataAnnotations.StringLength(100)] public string? Specialization { get; set; }
    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "1000000")] public decimal? ConsultationFee { get; set; }
    [System.ComponentModel.DataAnnotations.Range(5, 240)] public int DurationMinutes { get; set; } = 20;
    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0.1", "10")] public decimal GraceMultiplier { get; set; } = 2m;
    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "5")] public decimal SafetyBufferMultiplier { get; set; } = 0.5m;
    [System.ComponentModel.DataAnnotations.Range(2, 30)] public int MinimumSamples { get; set; } = 5;
    [System.ComponentModel.DataAnnotations.Range(0, 3)] public int BookedCapacityReservation { get; set; } = 1;
}

public sealed class WaitingListInput
{
    public string? DoctorId { get; set; }
    [System.ComponentModel.DataAnnotations.DataType(System.ComponentModel.DataAnnotations.DataType.Date)] public DateOnly DateFrom { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    [System.ComponentModel.DataAnnotations.DataType(System.ComponentModel.DataAnnotations.DataType.Date)] public DateOnly DateTo { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1));
    public TimeOnly? TimeFrom { get; set; }
    public TimeOnly? TimeTo { get; set; }
}
