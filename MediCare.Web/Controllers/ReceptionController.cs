using System.Security.Claims;
using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MediCare.Web.Controllers;

[Authorize(Roles = MediCareRoles.Receptionist + "," + MediCareRoles.Admin)]
public sealed class ReceptionController(
    AppointmentService appointmentService,
    ICheckInService checkInService,
    IWalkInAdmissionApprovalService admissionApprovalService,
    IAccountProvisioningService accountProvisioningService,
    IReceptionQueueService queueService,
    IQueueBoardService queueBoardService,
    IClinicClock clinicClock,
    TimeZoneInfo clinicZone,
    IWorkingHourExtensionService extensionService,
    IDisruptionService disruptionService,
    IDoctorAvailabilityService availability,
    IAppointmentBookingFlow bookingFlow,
    IAppointmentQueryService appointmentQueries,
    IDoctorDirectoryService directory,
    IPasswordResetService passwordReset,
    ITemporaryPasswordMailer temporaryPasswordMailer,
    ILogger<ReceptionController> logger) : Controller
{
    private const int RecentDisruptionCount = 30;

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var appointments = await appointmentService.GetPendingAsync(cancellationToken);
        return View(appointments);
    }

    [HttpGet]
    [Authorize(Roles = MediCareRoles.Receptionist)]
    public IActionResult CreatePatient() => View(new PatientAccountViewModel());

    [HttpPost]
    [Authorize(Roles = MediCareRoles.Receptionist)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePatient(PatientAccountViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await accountProvisioningService.CreatePatientAsync(
            new CreatePatientAccountRequest(model.PhoneNumber, model.FullName), cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error);
            return View(model);
        }

        Response.Headers.CacheControl = "no-store, no-cache";
        return View("~/Views/Shared/AccountCreated.cshtml", new AccountCreatedViewModel
        {
            FullName = model.FullName,
            UserName = result.CanonicalPhone!,
            Role = MediCareRoles.Patient,
            TemporaryPassword = result.TemporaryPassword!
        });
    }

    /// <summary>Confirm step before resetting a patient's password. Reception or Admin only (class-level authorization).</summary>
    [HttpGet]
    public async Task<IActionResult> ConfirmResetPatientPassword(Guid id, CancellationToken cancellationToken)
    {
        var patient = await appointmentQueries.GetPatientAsync(id, cancellationToken);
        if (patient is null)
            return NotFound();

        return View(new PasswordResetConfirmModel { Id = id, FullName = patient.FullName, UserName = patient.PhoneNumber, Role = MediCareRoles.Patient });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPatientPassword(Guid id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
            return Forbid();

        var result = await passwordReset.ResetPatientPasswordAsync(id, actorId, cancellationToken);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Message;
            return RedirectToAction(nameof(PatientDetail), new { id });
        }

        logger.LogInformation("Account {ActorUserId} reset the password of Patient account {TargetUserId}.", actorId, result.UserId);
        Response.Headers.CacheControl = "no-store, no-cache";
        var mail = await temporaryPasswordMailer.SendAsync(
            result.UserId!.Value, result.TemporaryPassword!, $"{Request.Scheme}://{Request.Host}", cancellationToken);
        return View("~/Views/Shared/PasswordReset.cshtml", new AccountCreatedViewModel
        {
            EmailNotice = mail.Status switch
            {
                TemporaryPasswordMailStatus.Sent => $"We also emailed it to {mail.MaskedEmail}.",
                TemporaryPasswordMailStatus.NoEmail => "This account has no email address, so nothing was emailed.",
                TemporaryPasswordMailStatus.Unverified => "Their email was never confirmed, so nothing was emailed.",
                _ => "The email could not be sent. Give them the password directly."
            },
            FullName = result.FullName!,
            UserName = result.UserName!,
            Role = result.Role!,
            TemporaryPassword = result.TemporaryPassword!
        });
    }

    /// <summary>Works from the Requests page and the Appointments page; returnUrl brings Reception back to where they clicked.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(Guid id, string? returnUrl, CancellationToken cancellationToken)
    {
        try
        {
            await appointmentService.ConfirmAsync(id, cancellationToken);
            TempData["StatusMessage"] = "Appointment confirmed. The patient has been notified.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction(nameof(Index));
    }

    /// <summary>Works from the Requests page and the Appointments page; returnUrl brings Reception back to where they clicked.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid id, string? returnUrl, CancellationToken cancellationToken)
    {
        try
        {
            await appointmentService.RejectAsync(id, cancellationToken);
            TempData["StatusMessage"] = "Request rejected. The patient has been notified.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ScheduledCheckIn(ScheduledCheckInViewModel model, CancellationToken cancellationToken)
    {
        try
        {
            var result = await checkInService.CheckInScheduledPatientAsync(model.AppointmentId, model.ExpectedDoctorId, cancellationToken);
            TempData["StatusMessage"] = WithWarning(result.Message ?? "Scheduled patient checked in.", result.Warning);
        }
        catch (InvalidOperationException exception)
        {
            TempData["StatusMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterWalkIn(WalkInRegistrationViewModel model, CancellationToken cancellationToken)
    {
        try
        {
            await checkInService.RegisterWalkInAsync(model.PatientId, model.DoctorId, cancellationToken);
            TempData["StatusMessage"] = "Walk-in registered in the waiting queue.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["StatusMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestWalkInAdmission(WalkInAdmissionRequestViewModel model, CancellationToken cancellationToken)
    {
        try
        {
            await admissionApprovalService.RequestAsync(model.QueueEntryId, cancellationToken);
            TempData["StatusMessage"] = "Walk-in admission request sent to the doctor.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["StatusMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CallWalkIn(WalkInCallViewModel model, CancellationToken cancellationToken)
    {
        try
        {
            var result = await admissionApprovalService.CallAsync(model.QueueEntryId, model.ApprovalId, cancellationToken);
            TempData["StatusMessage"] = result.Message;
        }
        catch (InvalidOperationException exception)
        {
            TempData["StatusMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Queue(Guid? doctorId, CancellationToken cancellationToken)
    {
        // Read-only: due disruption cancellations are applied by the maintenance job and by Call Next, never by a GET.
        var doctors = await queueBoardService.GetActiveDoctorsAsync(cancellationToken);
        var selected = doctorId ?? doctors.FirstOrDefault()?.DoctorId;
        var board = selected is Guid id && doctors.Any(doctor => doctor.DoctorId == id)
            ? await queueBoardService.GetDoctorQueueBoardAsync(id, clinicClock.Today, cancellationToken)
            : null;

        return View(new QueueBoardPageViewModel
        {
            Doctors = doctors,
            SelectedDoctorId = board?.DoctorId,
            Board = board,
            ClinicDate = clinicClock.Today,
            ClinicZone = clinicZone
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> QueueCheckIn(Guid appointmentId, Guid doctorId, CancellationToken cancellationToken) =>
        RunQueueActionAsync(doctorId, async () =>
        {
            var result = await checkInService.CheckInScheduledPatientAsync(appointmentId, doctorId, cancellationToken);
            return WithWarning(result.Message ?? "Scheduled patient checked in.", result.Warning);
        });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> QueueRegisterWalkIn(string phoneNumber, Guid doctorId, CancellationToken cancellationToken) =>
        RunQueueActionAsync(doctorId, async () =>
        {
            var lookup = await queueService.FindPatientByPhoneAsync(phoneNumber ?? string.Empty, cancellationToken);
            if (lookup.PatientId is not Guid patientId)
            {
                if (lookup.NotRegistered)
                    TempData["CreatePatientHint"] = true;
                throw new InvalidOperationException(lookup.Error ?? "The patient could not be found.");
            }

            await checkInService.RegisterWalkInAsync(patientId, doctorId, cancellationToken);
            return "Walk-in registered in the waiting queue.";
        });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> QueueRequestApproval(Guid queueEntryId, Guid doctorId, CancellationToken cancellationToken) =>
        RunQueueActionAsync(doctorId, async () =>
        {
            await admissionApprovalService.RequestAsync(queueEntryId, cancellationToken);
            return "Walk-in admission request sent to the doctor.";
        });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> QueueCallWalkIn(Guid queueEntryId, Guid approvalId, Guid doctorId, CancellationToken cancellationToken) =>
        RunQueueActionAsync(doctorId, async () => (await admissionApprovalService.CallAsync(queueEntryId, approvalId, cancellationToken)).Message);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> QueueCallScheduled(Guid queueEntryId, Guid doctorId, CancellationToken cancellationToken) =>
        RunQueueActionAsync(doctorId, () => queueService.CallScheduledAsync(queueEntryId, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> QueueCallNext(Guid doctorId, CancellationToken cancellationToken) =>
        RunQueueActionAsync(doctorId, async () =>
        {
            var result = await queueService.CallNextAsync(doctorId, cancellationToken);
            if (!result.Called)
                throw new InvalidOperationException(result.Message);
            return result.Message;
        });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> QueueReturnToWaiting(Guid queueEntryId, Guid doctorId, CancellationToken cancellationToken) =>
        RunQueueActionAsync(doctorId, () => queueService.ReturnToWaitingAsync(queueEntryId, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> QueueCancel(Guid queueEntryId, Guid doctorId, CancellationToken cancellationToken) =>
        RunQueueActionAsync(doctorId, () => queueService.CancelQueueEntryAsync(queueEntryId, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Extensions(Guid? doctorId, DateOnly? date, CancellationToken cancellationToken)
    {
        var doctors = await extensionService.GetActiveDoctorsAsync(cancellationToken);
        var selected = doctorId is Guid id && doctors.Any(doctor => doctor.DoctorId == id) ? id : (Guid?)null;
        var today = clinicClock.Today;
        var model = new ReceptionExtensionsViewModel
        {
            Today = today,
            Doctors = doctors,
            SelectedDoctorId = selected,
            SelectedDate = date,
            Form = new ReceptionExtensionRequestModel { DoctorId = selected, Date = date ?? today }
        };

        if (selected is Guid doctor)
        {
            model.Extensions = await extensionService.GetForDoctorAsync(doctor, today.AddDays(-30), cancellationToken);
            model.WindowEnds = await extensionService.GetWindowEndTimesAsync(doctor, date ?? today, cancellationToken);
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestExtension(ReceptionExtensionRequestModel model, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var userId))
            return Forbid();

        if (!ModelState.IsValid || model.DoctorId is not Guid doctorId || model.Date is not DateOnly date ||
            model.OriginalEndTime is not TimeOnly original || model.ExtendedEndTime is not TimeOnly extended)
        {
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(entry => entry.Errors).Select(error => error.ErrorMessage).FirstOrDefault()
                ?? "Check the extension details and try again.";
            return RedirectToAction(nameof(Extensions), new { doctorId = model.DoctorId, date = model.Date?.ToString("yyyy-MM-dd") });
        }

        var result = await extensionService.RequestAsync(doctorId, date, original, extended, model.Reason, userId, cancellationToken);
        if (result.Succeeded)
            TempData["StatusMessage"] = result.Message;
        else
            TempData["ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Extensions), new { doctorId, date = date.ToString("yyyy-MM-dd") });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelExtension(Guid extensionId, Guid doctorId, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var userId))
            return Forbid();

        var result = await extensionService.CancelAsync(extensionId, userId, isDoctorOwner: false, cancellationToken);
        if (result.Succeeded)
            TempData["StatusMessage"] = result.Message;
        else
            TempData["ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Extensions), new { doctorId });
    }

    [HttpGet]
    public async Task<IActionResult> Disruptions(CancellationToken cancellationToken)
    {
        var doctors = await queueBoardService.GetActiveDoctorsAsync(cancellationToken);
        var recent = await disruptionService.GetRecentAsync(RecentDisruptionCount, cancellationToken);
        return View(new DisruptionsPageViewModel
        {
            Doctors = doctors,
            Recent = recent,
            Today = clinicClock.Today
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordDisruption(DisruptionRecordModel model, CancellationToken cancellationToken)
    {
        if (!TryGetApplicationUserId(out var userId))
            return Forbid();

        if (!ModelState.IsValid || model.DoctorId is not Guid doctorId || model.Date is not DateOnly date || model.FromTime is not TimeOnly fromTime)
        {
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(entry => entry.Errors).Select(error => error.ErrorMessage).FirstOrDefault()
                ?? "Check the disruption details and try again.";
            return RedirectToAction(nameof(Disruptions));
        }

        try
        {
            var disruptionId = await disruptionService.RecordAsync(doctorId, date, fromTime, model.Reason, userId, cancellationToken);
            TempData["StatusMessage"] = "Disruption recorded. The doctor's remaining time is blocked and the worklist is ready.";
            return RedirectToAction(nameof(Disruption), new { id = disruptionId });
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            TempData["ErrorMessage"] = exception.Message;
            return RedirectToAction(nameof(Disruptions));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Disruption(
        Guid id,
        Guid? rescheduleItemId,
        Guid? rescheduleDoctorId,
        DateOnly? rescheduleDate,
        CancellationToken cancellationToken)
    {
        DisruptionWorklist worklist;
        try
        {
            worklist = await disruptionService.GetWorklistAsync(id, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
            return RedirectToAction(nameof(Disruptions));
        }

        var doctors = await queueBoardService.GetActiveDoctorsAsync(cancellationToken);
        IReadOnlyList<TimeOnly> slots = [];
        if (rescheduleDoctorId is Guid slotDoctorId && rescheduleDate is DateOnly slotDate &&
            doctors.Any(doctor => doctor.DoctorId == slotDoctorId))
        {
            slots = await availability.GetAvailableSlotsAsync(slotDoctorId, slotDate, cancellationToken);
        }

        return View(new DisruptionDetailPageViewModel
        {
            Worklist = worklist,
            Today = clinicClock.Today,
            Doctors = doctors,
            RescheduleItemId = rescheduleItemId,
            RescheduleDoctorId = rescheduleDoctorId,
            RescheduleDate = rescheduleDate,
            AvailableSlots = slots
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> LogDisruptionContact(Guid disruptionId, Guid itemId, string? notes, CancellationToken cancellationToken) =>
        RunDisruptionActionAsync(disruptionId, async userId =>
        {
            await disruptionService.LogContactAsync(itemId, userId, notes, cancellationToken);
            return "Contact logged. The patient stays on the worklist until rescheduled or cancelled.";
        });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RescheduleDisruptionItem(
        Guid disruptionId,
        Guid itemId,
        Guid doctorId,
        DateOnly date,
        TimeOnly startTime,
        string? notes,
        CancellationToken cancellationToken) =>
        RunDisruptionActionAsync(disruptionId, async userId =>
        {
            await disruptionService.RescheduleFromWorklistAsync(itemId, userId, doctorId, date, startTime, notes, cancellationToken);
            return "Appointment rescheduled. The patient was notified.";
        });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CancelDisruptionItem(Guid disruptionId, Guid itemId, string? notes, CancellationToken cancellationToken) =>
        RunDisruptionActionAsync(disruptionId, async userId =>
        {
            await disruptionService.CancelFromWorklistAsync(itemId, userId, notes, cancellationToken);
            return "Patient cancelled with reason clinic unavailable. The patient was notified.";
        });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RecordDisruptionDayEnd(Guid disruptionId, CancellationToken cancellationToken) =>
        RunDisruptionActionAsync(disruptionId, async userId =>
        {
            await disruptionService.RecordDayEndAsync(disruptionId, userId, cancellationToken);
            return "Day end recorded. Unresolved appointments were cancelled as clinic unavailable and patients were notified.";
        });

    /// <summary>Runs one worklist action as the signed-in user and returns to the disruption page with a PRG message.</summary>
    private async Task<IActionResult> RunDisruptionActionAsync(Guid disruptionId, Func<Guid, Task<string>> action)
    {
        if (!TryGetApplicationUserId(out var userId))
            return Forbid();

        try
        {
            TempData["StatusMessage"] = await action(userId);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Disruption), new { id = disruptionId });
    }

    [HttpGet]
    public async Task<IActionResult> Patients([FromQuery] PatientSearchModel search, CancellationToken cancellationToken)
    {
        // A search that cannot be right (half a name, a full number with a typo) says what to fix instead of "no matches".
        search.Problem = InputChecks.PatientSearchProblem(search.Query);
        search.Results = search.Problem is null
            ? await appointmentQueries.SearchPatientsAsync(search.Query, cancellationToken)
            : [];
        return View(search);
    }

    [HttpGet]
    public async Task<IActionResult> PatientDetail(Guid id, Guid? doctorId, DateOnly? date, CancellationToken cancellationToken)
    {
        var patient = await appointmentQueries.GetPatientAsync(id, cancellationToken);
        if (patient is null)
            return NotFound();

        var today = clinicClock.Today;
        var selectedDate = date ?? today;
        var doctors = await queueBoardService.GetActiveDoctorsAsync(cancellationToken);
        var selectedDoctor = doctorId is Guid chosen && doctors.Any(doctor => doctor.DoctorId == chosen) ? chosen : (Guid?)null;
        IReadOnlyList<TimeOnly> slots = selectedDoctor is Guid doctor && selectedDate >= today
            ? await availability.GetAvailableSlotsAsync(doctor, selectedDate, cancellationToken)
            : [];

        return View(new PatientDetailModel
        {
            Patient = patient,
            Appointments = await appointmentQueries.GetPatientAppointmentsAsync(id, cancellationToken),
            Doctors = doctors,
            DoctorId = selectedDoctor,
            Date = selectedDate,
            Today = today,
            Slots = slots
        });
    }

    [HttpGet]
    public async Task<IActionResult> BookForPatient(Guid patientId, Guid doctorId, DateOnly date, TimeOnly time, CancellationToken cancellationToken)
    {
        var patient = await appointmentQueries.GetPatientAsync(patientId, cancellationToken);
        if (patient is null)
            return NotFound();

        var booking = await BuildClinicConfirmationAsync(doctorId, date, time, error: null, alternatives: [], cancellationToken);
        return booking is null ? NotFound() : View(new ClinicBookingConfirmationModel { Patient = patient, Booking = booking });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BookForPatient(ClinicBookingForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Check the booking details and try again.";
            return RedirectToAction(nameof(Patients));
        }

        var outcome = await bookingFlow.BookForClinicAsync(form.PatientId, form.DoctorId, form.Date, form.Time, ClinicActorResolver.From(User), cancellationToken);
        if (outcome.Succeeded)
        {
            TempData["StatusMessage"] = "Appointment booked and confirmed.";
            return RedirectToAction(nameof(PatientDetail), new { id = form.PatientId });
        }

        var patient = await appointmentQueries.GetPatientAsync(form.PatientId, cancellationToken);
        var booking = await BuildClinicConfirmationAsync(form.DoctorId, form.Date, form.Time, outcome.Error, outcome.Alternatives, cancellationToken);
        if (patient is null || booking is null)
            return NotFound();
        return View(nameof(BookForPatient), new ClinicBookingConfirmationModel { Patient = patient, Booking = booking });
    }

    [HttpGet]
    public async Task<IActionResult> Appointments(DateOnly? date, Guid? doctorId, CancellationToken cancellationToken)
    {
        var today = clinicClock.Today;
        var doctors = await queueBoardService.GetActiveDoctorsAsync(cancellationToken);
        var doctorFilter = doctorId is Guid chosen && doctors.Any(doctor => doctor.DoctorId == chosen) ? chosen : (Guid?)null;
        var selectedDate = date ?? today;

        return View(new AppointmentDayModel
        {
            Date = selectedDate,
            DoctorId = doctorFilter,
            Today = today,
            Doctors = doctors,
            Rows = await appointmentQueries.GetDayAsync(selectedDate, doctorFilter, cancellationToken)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelAppointment(ClinicCancelForm form, CancellationToken cancellationToken)
    {
        var date = form.Date == default ? clinicClock.Today : form.Date;
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(entry => entry.Errors).Select(error => error.ErrorMessage).FirstOrDefault()
                ?? "A cancellation reason is required.";
            return RedirectToAction(nameof(Appointments), new { date, doctorId = form.DoctorId });
        }

        try
        {
            // The actor comes from the signed-in role, never from the form.
            await appointmentService.CancelByClinicAsync(form.AppointmentId, ClinicActorResolver.From(User), form.Reason.Trim(), cancellationToken);
            TempData["StatusMessage"] = "Appointment cancelled with the reason recorded.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Appointments), new { date, doctorId = form.DoctorId });
    }

    [HttpGet]
    public async Task<IActionResult> RescheduleAppointment(Guid id, Guid? doctorId, DateOnly? date, CancellationToken cancellationToken)
    {
        var original = await appointmentQueries.GetAppointmentAsync(id, cancellationToken);
        if (original is null)
            return NotFound();
        if (!original.CanClinicReschedule)
        {
            TempData["ErrorMessage"] = "Only a confirmed appointment from today onward with no live replacement can be rescheduled.";
            return RedirectToAction(nameof(Appointments));
        }

        return View(await BuildClinicRescheduleAsync(original, doctorId, date, error: null, alternatives: [], cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RescheduleAppointment(ClinicRescheduleForm form, CancellationToken cancellationToken)
    {
        var outcome = await bookingFlow.RescheduleByClinicAsync(form.AppointmentId, form.DoctorId, form.Date, form.Time, ClinicActorResolver.From(User), cancellationToken);
        if (outcome.Succeeded)
        {
            TempData["StatusMessage"] = "Appointment rescheduled. The replacement is confirmed and the original is marked Rescheduled.";
            return RedirectToAction(nameof(Appointments), new { date = form.Date });
        }

        var original = await appointmentQueries.GetAppointmentAsync(form.AppointmentId, cancellationToken);
        if (original is null)
            return NotFound();
        return View(nameof(RescheduleAppointment), await BuildClinicRescheduleAsync(original, form.DoctorId, form.Date, outcome.Error, outcome.Alternatives, cancellationToken));
    }

    private async Task<BookingConfirmationModel?> BuildClinicConfirmationAsync(
        Guid doctorId,
        DateOnly date,
        TimeOnly time,
        string? error,
        IReadOnlyList<TimeOnly> alternatives,
        CancellationToken cancellationToken)
    {
        var profile = await directory.GetProfileAsync(doctorId, date, cancellationToken);
        if (profile is null)
            return null;

        return new BookingConfirmationModel
        {
            Profile = profile,
            Date = date,
            Time = time,
            EndTime = time.AddMinutes(profile.AppointmentDurationOnDateMinutes),
            IsSlotFree = profile.FreeSlots.Contains(time),
            Error = error,
            Alternatives = alternatives
        };
    }

    private async Task<ClinicRescheduleModel> BuildClinicRescheduleAsync(
        AppointmentRow original,
        Guid? doctorId,
        DateOnly? date,
        string? error,
        IReadOnlyList<TimeOnly> alternatives,
        CancellationToken cancellationToken)
    {
        var today = clinicClock.Today;
        var doctors = await queueBoardService.GetActiveDoctorsAsync(cancellationToken);
        var selectedDoctor = doctorId is Guid chosen && doctors.Any(doctor => doctor.DoctorId == chosen) ? chosen : original.Record.DoctorId;
        var selectedDate = date ?? original.Record.Date;
        var slots = selectedDate < today
            ? []
            : await availability.GetAvailableSlotsAsync(selectedDoctor, selectedDate, cancellationToken);

        return new ClinicRescheduleModel
        {
            Original = original,
            Doctors = doctors,
            DoctorId = selectedDoctor,
            Date = selectedDate,
            Today = today,
            Slots = slots,
            Error = error,
            Alternatives = alternatives
        };
    }

    private static string WithWarning(string message, string? warning) =>
        string.IsNullOrWhiteSpace(warning) ? message : $"{message} Warning: {warning}";

    private bool TryGetApplicationUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    /// <summary>Runs one queue action and returns to the board for the doctor. Expected refusals surface as TempData errors.</summary>
    private async Task<IActionResult> RunQueueActionAsync(Guid doctorId, Func<Task<string>> action)
    {
        try
        {
            TempData["StatusMessage"] = await action();
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Queue), new { doctorId });
    }

}
