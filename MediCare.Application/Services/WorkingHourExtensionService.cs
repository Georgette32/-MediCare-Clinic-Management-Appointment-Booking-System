using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

public sealed class WorkingHourExtensionService(
    IAppointmentRepository appointments,
    IDoctorScheduleRepository schedule,
    IUnitOfWork unitOfWork,
    IClinicClock clinicClock,
    IStaffNotificationPublisher? staffNotifications = null) : IWorkingHourExtensionService
{
    private readonly IStaffNotificationPublisher _staff = staffNotifications ?? SilentStaffNotificationPublisher.Instance;

    public Task<WorkingHourExtensionResult> RequestAsync(
        Guid doctorId, DateOnly date, TimeOnly originalEndTime, TimeOnly extendedEndTime, string reason, Guid requestedByUserId, CancellationToken cancellationToken) =>
        RunSafelyAsync(async () =>
        {
            var doctor = await appointments.GetDoctorAsync(doctorId, cancellationToken)
                ?? throw new InvalidOperationException("The active doctor was not found.");
            var result = await unitOfWork.ExecuteDoctorQueueWithRetryAsync(doctor.Id, date, async ct =>
            {
                await ValidateWindowAsync(doctor.Id, date, originalEndTime, extendedEndTime, checkLiveDuplicate: true, ct);
                var extension = DoctorWorkingHourExtension.Request(doctor.Id, date, originalEndTime, extendedEndTime, reason, requestedByUserId, clinicClock.UtcNow);
                await schedule.AddExtensionAsync(extension, ct);
                return new WorkingHourExtensionResult(true, "Extension request recorded. It takes effect only after the doctor approves it.", extension.Id);
            }, cancellationToken);

            // Published after the commit; the publisher logs and swallows delivery failures.
            await _staff.PublishToDoctorAsync(
                doctor.Id,
                StaffNotificationKind.WorkingHourExtensionRequested,
                "Working-hour extension requested",
                $"Reception requested extra hours on {date:yyyy-MM-dd} (until {extendedEndTime:HH\\:mm}). Review it on the Extensions page.",
                nameof(DoctorWorkingHourExtension),
                result.ExtensionId,
                cancellationToken);
            return result;
        });

    public Task<WorkingHourExtensionResult> CreateOwnAsync(
        Guid doctorApplicationUserId, DateOnly date, TimeOnly originalEndTime, TimeOnly extendedEndTime, string reason, CancellationToken cancellationToken) =>
        RunSafelyAsync(async () =>
        {
            var doctor = await GetDoctorForUserAsync(doctorApplicationUserId, cancellationToken);
            return await unitOfWork.ExecuteDoctorQueueWithRetryAsync(doctor.Id, date, async ct =>
            {
                await ValidateWindowAsync(doctor.Id, date, originalEndTime, extendedEndTime, checkLiveDuplicate: true, ct);
                var extension = DoctorWorkingHourExtension.CreateApprovedByDoctor(
                    doctor.Id, date, originalEndTime, extendedEndTime, reason, doctorApplicationUserId, clinicClock.UtcNow);
                await schedule.AddExtensionAsync(extension, ct);
                return new WorkingHourExtensionResult(true, "Extension approved and added to your schedule.", extension.Id);
            }, cancellationToken);
        });

    public Task<WorkingHourExtensionResult> DecideAsync(Guid extensionId, Guid doctorApplicationUserId, bool approve, string? notes, CancellationToken cancellationToken) =>
        RunSafelyAsync(async () =>
        {
            var preview = await schedule.GetExtensionByIdAsync(extensionId, cancellationToken)
                ?? throw new InvalidOperationException("The extension request was not found.");
            var doctor = await GetDoctorForUserAsync(doctorApplicationUserId, cancellationToken);
            if (preview.DoctorId != doctor.Id)
                throw new InvalidOperationException("Doctors may decide only extension requests for their own schedule.");

            var decided = await unitOfWork.ExecuteDoctorQueueWithRetryAsync(preview.DoctorId, preview.Date, async ct =>
            {
                var extension = await schedule.GetExtensionByIdAsync(extensionId, ct)
                    ?? throw new InvalidOperationException("The extension request was not found.");
                if (extension.DoctorId != doctor.Id)
                    throw new InvalidOperationException("Doctors may decide only extension requests for their own schedule.");
                if (extension.Status != WorkingHourExtensionStatus.Pending)
                    throw new InvalidOperationException("This extension request has already been decided or cancelled.");
                if (extension.IsExpiredPending(clinicClock.Today))
                    throw new InvalidOperationException("This extension request has expired and can no longer be decided.");

                var now = clinicClock.UtcNow;
                if (approve)
                {
                    // Re-check the window at approval time: the extra hours must still fit the schedule.
                    await ValidateWindowAsync(extension.DoctorId, extension.Date, extension.OriginalEndTime, extension.ExtendedEndTime,
                        checkLiveDuplicate: false, ct);
                    extension.Approve(now, notes);
                    return new WorkingHourExtensionResult(true, "Extension approved. The extra availability is now offered.", extension.Id);
                }

                extension.Reject(now, notes);
                return new WorkingHourExtensionResult(true, "Extension request rejected.", extension.Id);
            }, cancellationToken);

            // The requester (Reception) hears about the decision once it is committed.
            var outcome = approve ? "approved" : "rejected";
            await _staff.PublishToUserAsync(
                preview.RequestedByUserId,
                StaffNotificationKind.WorkingHourExtensionDecided,
                $"Extension {outcome}",
                $"Your working-hour extension request for {preview.Date:yyyy-MM-dd} (until {preview.ExtendedEndTime:HH\\:mm}) was {outcome}.",
                nameof(DoctorWorkingHourExtension),
                decided.ExtensionId,
                cancellationToken);
            return decided;
        });

    public Task<WorkingHourExtensionResult> CancelAsync(Guid extensionId, Guid actorUserId, bool isDoctorOwner, CancellationToken cancellationToken) =>
        RunSafelyAsync(async () =>
        {
            var preview = await schedule.GetExtensionByIdAsync(extensionId, cancellationToken)
                ?? throw new InvalidOperationException("The extension was not found.");

            return await unitOfWork.ExecuteDoctorQueueWithRetryAsync(preview.DoctorId, preview.Date, async ct =>
            {
                var extension = await schedule.GetExtensionByIdAsync(extensionId, ct)
                    ?? throw new InvalidOperationException("The extension was not found.");

                if (isDoctorOwner)
                {
                    var doctor = await GetDoctorForUserAsync(actorUserId, ct);
                    if (extension.DoctorId != doctor.Id)
                        throw new InvalidOperationException("Doctors may cancel only extensions of their own schedule.");
                }
                else if (extension.Status != WorkingHourExtensionStatus.Pending)
                {
                    throw new InvalidOperationException("Reception and Admin can cancel only pending extension requests.");
                }

                if (extension.Status == WorkingHourExtensionStatus.Approved &&
                    await schedule.HasLiveAppointmentOverlapAsync(extension.DoctorId, extension.Date, extension.OriginalEndTime, extension.ExtendedEndTime, ct))
                {
                    throw new InvalidOperationException(
                        "Patients are already booked inside the extended hours. Reschedule or cancel those appointments before cancelling the extension.");
                }

                extension.Cancel(clinicClock.UtcNow);
                return new WorkingHourExtensionResult(true, "Extension cancelled.", extension.Id);
            }, cancellationToken);
        });

    public async Task<IReadOnlyList<WorkingHourExtensionView>> GetForDoctorAsync(Guid doctorId, DateOnly fromDate, CancellationToken cancellationToken)
    {
        var today = clinicClock.Today;
        var items = await schedule.GetForDoctorFromDateAsync(doctorId, fromDate, cancellationToken);
        return items.Select(item => ToView(item, today)).ToArray();
    }

    public async Task<IReadOnlyList<WorkingHourExtensionView>> GetOwnAsync(Guid doctorApplicationUserId, DateOnly fromDate, CancellationToken cancellationToken)
    {
        var doctor = await GetDoctorForUserAsync(doctorApplicationUserId, cancellationToken);
        return await GetForDoctorAsync(doctor.Id, fromDate, cancellationToken);
    }

    public async Task<IReadOnlyList<TimeOnly>> GetOwnWindowEndTimesAsync(Guid doctorApplicationUserId, DateOnly date, CancellationToken cancellationToken)
    {
        var doctor = await GetDoctorForUserAsync(doctorApplicationUserId, cancellationToken);
        return await GetWindowEndTimesAsync(doctor.Id, date, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkingHourExtensionView>> GetPendingForDoctorUserAsync(Guid doctorApplicationUserId, CancellationToken cancellationToken)
    {
        var doctor = await GetDoctorForUserAsync(doctorApplicationUserId, cancellationToken);
        var today = clinicClock.Today;
        var items = await schedule.GetForDoctorFromDateAsync(doctor.Id, today, cancellationToken);
        return items
            .Where(item => item.Status == WorkingHourExtensionStatus.Pending)
            .Select(item => ToView(item, today))
            .Where(view => !view.IsExpired)
            .ToArray();
    }

    public async Task<IReadOnlyList<TimeOnly>> GetWindowEndTimesAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken)
    {
        if (await appointments.IsHolidayAsync(doctorId, date, cancellationToken))
            return [];
        var hours = await appointments.GetWorkingHoursAsync(doctorId, date.DayOfWeek, cancellationToken);
        return hours.Select(hour => hour.EndTime).Distinct().OrderBy(end => end).ToArray();
    }

    public async Task<IReadOnlyList<DoctorOption>> GetActiveDoctorsAsync(CancellationToken cancellationToken)
    {
        var doctors = await schedule.GetActiveDoctorsAsync(cancellationToken);
        return doctors
            .OrderBy(doctor => doctor.FullName)
            .Select(doctor => new DoctorOption(doctor.Id, doctor.FullName))
            .ToArray();
    }

    /// <summary>
    /// Shared rules for recording an extension. Runs inside the doctor-queue transaction so concurrent requests for the
    /// same doctor and date are serialized. Throws InvalidOperationException with a user-facing message on any violation.
    /// </summary>
    private async Task ValidateWindowAsync(Guid doctorId, DateOnly date, TimeOnly originalEndTime, TimeOnly extendedEndTime, bool checkLiveDuplicate, CancellationToken cancellationToken)
    {
        if (date < clinicClock.Today)
            throw new InvalidOperationException("Extensions can be recorded only for today or a later date.");
        if (extendedEndTime <= originalEndTime)
            throw new InvalidOperationException("The extended end time must be later than the window's current end time.");
        if (await appointments.IsHolidayAsync(doctorId, date, cancellationToken))
            throw new InvalidOperationException("The doctor is on holiday on the selected date, so hours cannot be extended.");

        var windows = await appointments.GetWorkingHoursAsync(doctorId, date.DayOfWeek, cancellationToken);
        if (!windows.Any(window => window.EndTime == originalEndTime))
            throw new InvalidOperationException("The original end time does not match an active working window on that day.");

        var overlapping = windows.FirstOrDefault(window =>
            window.EndTime != originalEndTime && window.StartTime < extendedEndTime && window.EndTime > originalEndTime);
        if (overlapping is not null)
        {
            throw new InvalidOperationException(
                $"The extended hours would overlap another working window ({overlapping.StartTime:HH\\:mm}-{overlapping.EndTime:HH\\:mm}).");
        }

        if (checkLiveDuplicate && await schedule.HasLiveExtensionAsync(doctorId, date, originalEndTime, cancellationToken))
            throw new InvalidOperationException("A live extension already exists for this window.");
    }

    private async Task<Doctor> GetDoctorForUserAsync(Guid doctorApplicationUserId, CancellationToken cancellationToken) =>
        await appointments.GetDoctorByApplicationUserIdAsync(doctorApplicationUserId, cancellationToken)
        ?? throw new InvalidOperationException("The authenticated account is not linked to an active doctor profile.");

    private static WorkingHourExtensionView ToView(DoctorWorkingHourExtension item, DateOnly today) =>
        new(item.Id, item.DoctorId, item.Date, item.OriginalEndTime, item.ExtendedEndTime, item.Reason, item.Status,
            item.IsExpiredPending(today), item.DecisionNotes, item.RequestedAtUtc, item.DecidedAtUtc);

    private static async Task<WorkingHourExtensionResult> RunSafelyAsync(Func<Task<WorkingHourExtensionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (InvalidOperationException exception)
        {
            return new WorkingHourExtensionResult(false, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return new WorkingHourExtensionResult(false, exception.Message);
        }
    }
}
