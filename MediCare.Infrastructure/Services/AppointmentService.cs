using MediCare.Application;
using MediCare.Domain;
using MediCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Services;

public sealed class AppointmentService(AppointmentDbContext db, IAppointmentNotifier notifier) : IAppointmentService
{
    private static readonly TimeSpan OfferLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyList<DateTimeOffset>> GetAvailableSlotsAsync(string doctorId, DateOnly date, CancellationToken ct = default)
    {
        var working = await db.WorkingHours.SingleOrDefaultAsync(w => w.DoctorId == doctorId && w.DayOfWeek == date.DayOfWeek, ct);
        if (working is null) return [];
        var settings = await Settings(doctorId, ct);
        var slotMinutes = SchedulingRules.EffectiveSlotMinutes(settings, await DurationStats(doctorId, ct));
        var dayStart = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var start = dayStart.Add(working.StartTime.ToTimeSpan());
        var endOfDay = dayStart.Add(working.EndTime.ToTimeSpan());
        var appointments = await db.Appointments.AsNoTracking().Where(a => a.DoctorId == doctorId && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed) && a.ScheduledStart < endOfDay && a.ScheduledEnd > start).ToListAsync(ct);
        var breaks = await db.Breaks.AsNoTracking().Where(b => b.DoctorId == doctorId).ToListAsync(ct);
        var leaves = await db.Leaves.AsNoTracking().Where(l => l.DoctorId == doctorId && l.IsApproved && l.StartsAt < endOfDay && l.EndsAt > start).ToListAsync(ct);
        var slots = new List<DateTimeOffset>();
        var now = DateTimeOffset.UtcNow;
        for (var slot = start; slot.AddMinutes(slotMinutes) <= endOfDay; slot = slot.AddMinutes(slotMinutes))
        {
            var slotEnd = slot.AddMinutes(slotMinutes);
            if (slot <= now || appointments.Any(a => a.ScheduledStart < slotEnd && a.ScheduledEnd > slot)) continue;
            if (leaves.Any(l => l.StartsAt < slotEnd && l.EndsAt > slot)) continue;
            if (breaks.Any(b => b.IsRecurring
                ? b.RecurringDay == date.DayOfWeek && b.RecurringStart < TimeOnly.FromDateTime(slotEnd.UtcDateTime) && b.RecurringEnd > TimeOnly.FromDateTime(slot.UtcDateTime)
                : b.StartsAt < slotEnd && b.EndsAt > slot)) continue;
            slots.Add(slot);
        }
        return slots;
    }

    public async Task<IReadOnlyList<Appointment>> GetPatientAppointmentsAsync(string patientId, DateOnly? from = null, DateOnly? to = null, AppointmentStatus? status = null, CancellationToken ct = default)
    {
        var query = db.Appointments.AsNoTracking().Where(a => a.PatientId == patientId);
        if (from.HasValue) query = query.Where(a => a.ScheduledStart >= new DateTimeOffset(from.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        if (to.HasValue) query = query.Where(a => a.ScheduledStart < new DateTimeOffset(to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        if (status.HasValue) query = query.Where(a => a.Status == status.Value);
        return await query.OrderBy(a => a.ScheduledStart).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AppointmentOffer>> GetPatientOffersAsync(string patientId, CancellationToken ct = default) =>
        await db.Offers.AsNoTracking().Where(o => o.PatientId == patientId).OrderByDescending(o => o.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<AppointmentNotification>> GetPatientNotificationsAsync(string patientId, CancellationToken ct = default) =>
        await db.AppointmentNotifications.AsNoTracking().Where(n => n.PatientId == patientId).OrderByDescending(n => n.CreatedAt).Take(100).ToListAsync(ct);

    public async Task MarkNotificationReadAsync(long notificationId, string patientId, CancellationToken ct = default)
    {
        var item = await db.AppointmentNotifications.SingleOrDefaultAsync(n => n.Id == notificationId && n.PatientId == patientId, ct) ?? throw new KeyNotFoundException("Notification not found.");
        item.ReadAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Appointment>> GetDoctorAppointmentsAsync(string doctorId, DateOnly date, CancellationToken ct = default) =>
        await db.Appointments.AsNoTracking().Where(a => a.DoctorId == doctorId && a.ScheduledStart >= date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) && a.ScheduledStart < date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).OrderBy(a => a.ScheduledStart).ToListAsync(ct);

    public async Task<IReadOnlyList<ScheduleEvent>> GetScheduleEventsAsync(string doctorId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to <= from || to - from > TimeSpan.FromDays(93)) throw new ArgumentOutOfRangeException(nameof(to), "Calendar range must be between 0 and 93 days.");
        return await db.Appointments.AsNoTracking().Where(a => a.DoctorId == doctorId && a.ScheduledStart < to && a.ScheduledEnd > from)
            .OrderBy(a => a.ScheduledStart).Select(a => new ScheduleEvent(a.Id, a.PatientId, a.ScheduledStart, a.ScheduledEnd, a.Status.ToString())).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DoctorDirectoryItem>> SearchDoctorsAsync(string? search, CancellationToken ct = default)
    {
        var query = from user in db.Users
                    join userRole in db.UserRoles on user.Id equals userRole.UserId
                    join role in db.Roles on userRole.RoleId equals role.Id
                    where role.Name == "Doctor"
                    join setting in db.SchedulingSettings on user.Id equals setting.DoctorId into doctorSettings
                    from setting in doctorSettings.DefaultIfEmpty()
                    select new { user.Id, DisplayName = setting!.PublicDisplayName, Specialization = setting!.Specialization, Fee = setting!.ConsultationFee, Duration = (int?)setting!.DefaultConsultationDurationMinutes };
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Id.Contains(search) || (x.DisplayName != null && x.DisplayName.Contains(search)) || (x.Specialization != null && x.Specialization.Contains(search)));
        return await query.OrderBy(x => x.DisplayName).Take(100)
            .Select(x => new DoctorDirectoryItem(x.Id, x.DisplayName ?? x.Id, x.Specialization, x.Fee, x.Duration, x.Duration != null)).ToListAsync(ct);
    }

    public Task<DoctorSchedulingSettings?> GetDoctorSettingsAsync(string doctorId, CancellationToken ct = default) =>
        db.SchedulingSettings.AsNoTracking().SingleOrDefaultAsync(s => s.DoctorId == doctorId, ct);

    public async Task<IReadOnlyList<DoctorWorkingHour>> GetDoctorWorkingHoursAsync(string doctorId, CancellationToken ct = default) =>
        await db.WorkingHours.AsNoTracking().Where(x => x.DoctorId == doctorId).OrderBy(x => x.DayOfWeek).ToListAsync(ct);

    public async Task<IReadOnlyList<DoctorBreak>> GetDoctorBreaksAsync(string doctorId, CancellationToken ct = default) =>
        await db.Breaks.AsNoTracking().Where(x => x.DoctorId == doctorId).OrderBy(x => x.StartsAt).ToListAsync(ct);

    public async Task<IReadOnlyList<DoctorLeave>> GetDoctorLeavesAsync(string doctorId, CancellationToken ct = default) =>
        await db.Leaves.AsNoTracking().Where(x => x.DoctorId == doctorId).OrderBy(x => x.StartsAt).ToListAsync(ct);

    public async Task SaveDoctorSettingsAsync(DoctorSchedulingSettings settings, CancellationToken ct = default)
    {
        if (settings.DefaultConsultationDurationMinutes is < 5 or > 240) throw new InvalidOperationException("Consultation duration must be between 5 and 240 minutes.");
        if (settings.GracePeriodMultiplier <= 0 || settings.SafetyBufferMultiplier < 0) throw new InvalidOperationException("Scheduling multipliers must be nonnegative.");
        settings.CallTimeoutMinutes = 5;
        settings.WaitingRoomCapacity = 3;
        if (settings.MinimumHistorySamples < 2) settings.MinimumHistorySamples = 5;
        var current = await db.SchedulingSettings.SingleOrDefaultAsync(s => s.DoctorId == settings.DoctorId, ct);
        if (current is null) db.SchedulingSettings.Add(settings);
        else
        {
            current.DefaultConsultationDurationMinutes = settings.DefaultConsultationDurationMinutes;
            current.PublicDisplayName = settings.PublicDisplayName?.Trim();
            current.Specialization = settings.Specialization?.Trim();
            current.ConsultationFee = settings.ConsultationFee;
            current.GracePeriodMultiplier = settings.GracePeriodMultiplier;
            current.SafetyBufferMultiplier = settings.SafetyBufferMultiplier;
            current.MinimumHistorySamples = settings.MinimumHistorySamples;
            current.BookedCapacityReservation = Math.Clamp(settings.BookedCapacityReservation, 0, 3);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveWorkingHourAsync(string doctorId, DayOfWeek day, TimeOnly start, TimeOnly end, CancellationToken ct = default)
    {
        if (end <= start) throw new InvalidOperationException("Working-hours end must be after the start.");
        var scheduled = await db.Appointments.AsNoTracking().Where(a => a.DoctorId == doctorId && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed)).Select(a => new { a.ScheduledStart, a.ScheduledEnd }).ToListAsync(ct);
        if (scheduled.Any(a => a.ScheduledStart.DayOfWeek == day && (TimeOnly.FromDateTime(a.ScheduledStart.UtcDateTime) < start || TimeOnly.FromDateTime(a.ScheduledEnd.UtcDateTime) > end)))
            throw new InvalidOperationException("Existing appointments fall outside those working hours. Reschedule them before changing the hours.");
        var hour = await db.WorkingHours.SingleOrDefaultAsync(w => w.DoctorId == doctorId && w.DayOfWeek == day, ct);
        if (hour is null) db.WorkingHours.Add(new DoctorWorkingHour { DoctorId = doctorId, DayOfWeek = day, StartTime = start, EndTime = end });
        else { hour.StartTime = start; hour.EndTime = end; }
        await db.SaveChangesAsync(ct);
    }

    public async Task AddBreakAsync(DoctorBreak item, CancellationToken ct = default)
    {
        if (item.EndsAt <= item.StartsAt) throw new InvalidOperationException("Break end must be after the start.");
        if (item.IsRecurring && item.RecurringDay is null) throw new InvalidOperationException("Choose a weekday for a recurring break.");
        if (item.IsRecurring)
        {
            item.RecurringStart = TimeOnly.FromDateTime(item.StartsAt.UtcDateTime);
            item.RecurringEnd = TimeOnly.FromDateTime(item.EndsAt.UtcDateTime);
            if (item.RecurringEnd <= item.RecurringStart) throw new InvalidOperationException("A recurring break must end on the same day after it starts.");
        }
        var scheduled = await db.Appointments.AsNoTracking().Where(a => a.DoctorId == item.DoctorId && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed)).Select(a => new { a.ScheduledStart, a.ScheduledEnd }).ToListAsync(ct);
        var conflict = scheduled.Any(a => item.IsRecurring
            ? a.ScheduledStart.DayOfWeek == item.RecurringDay && TimeOnly.FromDateTime(a.ScheduledStart.UtcDateTime) < item.RecurringEnd && TimeOnly.FromDateTime(a.ScheduledEnd.UtcDateTime) > item.RecurringStart
            : a.ScheduledStart < item.EndsAt && a.ScheduledEnd > item.StartsAt);
        if (conflict) throw new InvalidOperationException("Existing appointments overlap this break. Reschedule them before adding it.");
        db.Breaks.Add(item); await db.SaveChangesAsync(ct);
    }

    public async Task AddLeaveAsync(DoctorLeave item, CancellationToken ct = default)
    {
        if (item.EndsAt <= item.StartsAt) throw new InvalidOperationException("Leave end must be after the start.");
        if (await db.Appointments.AnyAsync(a => a.DoctorId == item.DoctorId && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed) && a.ScheduledStart < item.EndsAt && a.ScheduledEnd > item.StartsAt, ct))
            throw new InvalidOperationException("Existing appointments overlap this leave. Reschedule them before approving leave.");
        item.IsApproved = true;
        db.Leaves.Add(item); await db.SaveChangesAsync(ct);
    }

    public async Task<Appointment> BookAsync(string patientId, string doctorId, DateTimeOffset start, CancellationToken ct = default)
    {
        if (start.Offset != TimeSpan.Zero) start = start.ToUniversalTime();
        if (start <= DateTimeOffset.UtcNow) throw new InvalidOperationException("Appointments must be booked for a future time.");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var settings = await db.SchedulingSettings.SingleOrDefaultAsync(s => s.DoctorId == doctorId, ct)
            ?? throw new InvalidOperationException("Doctor scheduling settings are not configured.");
        var history = await db.Appointments.Where(a => a.DoctorId == doctorId && a.Status == AppointmentStatus.Completed && a.ConsultationStartedAt != null && a.ConsultationEndedAt > a.ConsultationStartedAt)
            .OrderByDescending(a => a.ConsultationEndedAt).Take(30)
            .Select(a => new { a.ConsultationStartedAt, a.ConsultationEndedAt }).ToListAsync(ct);
        var stats = ConsultationStatistics.Calculate(history.Select(x => x.ConsultationEndedAt!.Value - x.ConsultationStartedAt!.Value));
        var minutes = SchedulingRules.EffectiveSlotMinutes(settings, stats);
        var end = start.AddMinutes(minutes);
        var working = await db.WorkingHours.SingleOrDefaultAsync(w => w.DoctorId == doctorId && w.DayOfWeek == start.DayOfWeek, ct);
        if (working is null || TimeOnly.FromDateTime(start.UtcDateTime) < working.StartTime || TimeOnly.FromDateTime(end.UtcDateTime) > working.EndTime)
            throw new InvalidOperationException("The appointment is outside the doctor's working hours.");
        var breaks = await db.Breaks.Where(b => b.DoctorId == doctorId && (b.IsRecurring || (b.StartsAt < end && b.EndsAt > start))).ToListAsync(ct);
        if (breaks.Any(b => b.IsRecurring
            ? b.RecurringDay == start.DayOfWeek && b.RecurringStart < TimeOnly.FromDateTime(end.UtcDateTime) && b.RecurringEnd > TimeOnly.FromDateTime(start.UtcDateTime)
            : b.StartsAt < end && b.EndsAt > start)) throw new InvalidOperationException("The appointment overlaps a doctor break.");
        if (await db.Leaves.AnyAsync(l => l.DoctorId == doctorId && l.IsApproved && l.StartsAt < end && l.EndsAt > start, ct))
            throw new InvalidOperationException("The doctor is on approved leave at that time.");
        if (await db.Appointments.AnyAsync(a => a.DoctorId == doctorId && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed) && a.ScheduledStart < end && a.ScheduledEnd > start, ct))
            throw new InvalidOperationException("That appointment slot is no longer available.");
        var appointment = new Appointment { PatientId = patientId, DoctorId = doctorId, ScheduledStart = start, ScheduledEnd = end, CreatedAt = DateTimeOffset.UtcNow, ConfirmedAt = DateTimeOffset.UtcNow };
        db.Appointments.Add(appointment);
        db.Events.Add(new AppointmentEvent { DoctorId = doctorId, PatientId = patientId, EventType = "AppointmentBooked" });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return appointment;
    }

    public async Task CancelAsync(long appointmentId, string patientId, string? reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var a = await db.Appointments.SingleOrDefaultAsync(x => x.Id == appointmentId && x.PatientId == patientId, ct)
            ?? throw new KeyNotFoundException("Appointment not found.");
        if (a.Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed)) throw new InvalidOperationException("This appointment cannot be cancelled.");
        if (a.CheckedInAt is not null) throw new InvalidOperationException("A checked-in appointment cannot be cancelled.");
        a.Status = AppointmentStatus.Cancelled; a.CancelledAt = DateTimeOffset.UtcNow; a.CancellationReason = reason; a.UpdatedAt = DateTimeOffset.UtcNow;
        db.Events.Add(new AppointmentEvent { DoctorId = a.DoctorId, PatientId = patientId, AppointmentId = a.Id, EventType = "AppointmentCancelled" });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await CreateOffersForCancelledSlotAsync(a, (int)OfferLifetime.TotalMinutes, ct);
    }

    public async Task RescheduleAsync(long appointmentId, string patientId, DateTimeOffset newStart, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var a = await db.Appointments.SingleOrDefaultAsync(x => x.Id == appointmentId && x.PatientId == patientId, ct) ?? throw new KeyNotFoundException("Appointment not found.");
        if (a.Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed) || a.CheckedInAt != null) throw new InvalidOperationException("This appointment can no longer be rescheduled.");
        newStart = newStart.ToUniversalTime();
        if (newStart <= DateTimeOffset.UtcNow) throw new InvalidOperationException("Appointments must be rescheduled to a future time.");
        var settings = await Settings(a.DoctorId, ct);
        var minutes = SchedulingRules.EffectiveSlotMinutes(settings, await DurationStats(a.DoctorId, ct));
        var newEnd = newStart.AddMinutes(minutes);
        var working = await db.WorkingHours.SingleOrDefaultAsync(w => w.DoctorId == a.DoctorId && w.DayOfWeek == newStart.DayOfWeek, ct);
        if (working is null || TimeOnly.FromDateTime(newStart.UtcDateTime) < working.StartTime || TimeOnly.FromDateTime(newEnd.UtcDateTime) > working.EndTime) throw new InvalidOperationException("The appointment is outside the doctor's working hours.");
        var scheduleBreaks = await db.Breaks.Where(b => b.DoctorId == a.DoctorId).ToListAsync(ct);
        if (scheduleBreaks.Any(b => b.IsRecurring
            ? b.RecurringDay == newStart.DayOfWeek && b.RecurringStart < TimeOnly.FromDateTime(newEnd.UtcDateTime) && b.RecurringEnd > TimeOnly.FromDateTime(newStart.UtcDateTime)
            : b.StartsAt < newEnd && b.EndsAt > newStart)) throw new InvalidOperationException("The appointment overlaps a doctor break.");
        if (await db.Leaves.AnyAsync(l => l.DoctorId == a.DoctorId && l.IsApproved && l.StartsAt < newEnd && l.EndsAt > newStart, ct)) throw new InvalidOperationException("The doctor is on approved leave at that time.");
        if (await db.Appointments.AnyAsync(x => x.Id != a.Id && x.DoctorId == a.DoctorId && (x.Status == AppointmentStatus.Pending || x.Status == AppointmentStatus.Confirmed) && x.ScheduledStart < newEnd && x.ScheduledEnd > newStart, ct)) throw new InvalidOperationException("That appointment slot is no longer available.");
        var oldStart = a.ScheduledStart;
        a.ScheduledStart = newStart; a.ScheduledEnd = newEnd; a.UpdatedAt = DateTimeOffset.UtcNow; a.ConfirmedAt = a.UpdatedAt;
        db.Events.Add(new AppointmentEvent { DoctorId = a.DoctorId, PatientId = patientId, AppointmentId = a.Id, EventType = "AppointmentRescheduled", Details = $"{oldStart:O} -> {newStart:O}" });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task CheckInAsync(long appointmentId, string patientId, DateTimeOffset arrivedAt, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var a = await db.Appointments.SingleOrDefaultAsync(x => x.Id == appointmentId && x.PatientId == patientId, ct) ?? throw new KeyNotFoundException("Appointment not found.");
        if (a.Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed) || a.CheckedInAt != null) throw new InvalidOperationException("Appointment cannot be checked in.");
        var settings = await Settings(a.DoctorId, ct);
        var stats = await DurationStats(a.DoctorId, ct);
        var consultation = SchedulingRules.EffectiveSlotMinutes(settings, stats);
        var withinGrace = SchedulingRules.IsWithinGrace(a.ScheduledStart, arrivedAt, consultation, settings.GracePeriodMultiplier);
        await EnsureSeatAvailable(a.DoctorId, settings, booked: true, ct);
        var activeSession = await db.DoctorQueueSessions.SingleOrDefaultAsync(s => s.DoctorId == a.DoctorId && s.Status == DoctorQueueSessionStatus.Active, ct);
        a.ArrivedAt = arrivedAt; a.CheckedInAt = arrivedAt;
        db.QueueEntries.Add(new QueueEntry { DoctorId = a.DoctorId, PatientId = patientId, AppointmentId = a.Id, DoctorQueueSessionId = activeSession?.Id, Type = withinGrace ? QueueType.Booked : QueueType.WalkIn, Approval = withinGrace ? WalkInApproval.Approved : WalkInApproval.Pending, ArrivalTime = arrivedAt, CheckInTime = arrivedAt, Status = QueueStatus.Waiting });
        db.Events.Add(new AppointmentEvent { DoctorId = a.DoctorId, PatientId = patientId, AppointmentId = a.Id, EventType = "PatientCheckedIn" });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task<QueueEntry> RegisterWalkInAsync(string doctorId, string patientId, DateTimeOffset arrival, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var session = await RequireActiveQueueSessionAsync(doctorId, ct);
        var e = new QueueEntry { DoctorId = doctorId, PatientId = patientId, DoctorQueueSessionId = session.Id, Type = QueueType.WalkIn, Approval = WalkInApproval.Pending, ArrivalTime = arrival, CheckInTime = arrival };
        db.QueueEntries.Add(e); db.Events.Add(new AppointmentEvent { DoctorId = doctorId, PatientId = patientId, EventType = "WalkInArrived" });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return e;
    }

    public async Task ApproveWalkInAsync(long queueEntryId, string doctorId, bool approve, bool defer, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var session = await RequireActiveQueueSessionAsync(doctorId, ct);
        var e = await db.QueueEntries.SingleOrDefaultAsync(q => q.Id == queueEntryId && q.DoctorId == doctorId && q.Type == QueueType.WalkIn && q.Approval == WalkInApproval.Pending, ct) ?? throw new KeyNotFoundException("Pending walk-in not found.");
        var settings = await Settings(doctorId, ct);
        if (approve) await EnsureSeatAvailable(doctorId, settings, booked: false, ct);
        e.Approval = defer ? WalkInApproval.Deferred : approve ? WalkInApproval.Approved : WalkInApproval.Rejected;
        e.DoctorQueueSessionId = session.Id;
        e.ApprovedByDoctorId = doctorId; e.ApprovalTime = DateTimeOffset.UtcNow;
        e.Status = approve && !defer ? QueueStatus.Waiting : defer ? QueueStatus.Deferred : QueueStatus.Rejected;
        db.Events.Add(new AppointmentEvent { DoctorId = doctorId, PatientId = e.PatientId, EventType = defer ? "WalkInDeferred" : approve ? "WalkInApproved" : "WalkInRejected" });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task<DoctorQueueSession> CreateQueueSessionAsync(string doctorId, CancellationToken ct = default)
    {
        _ = await Settings(doctorId, ct);
        var now = DateTimeOffset.UtcNow;
        var session = new DoctorQueueSession
        {
            DoctorId = doctorId,
            SessionDate = DateOnly.FromDateTime(now.UtcDateTime),
            CreatedAt = now,
            Status = DoctorQueueSessionStatus.NotStarted
        };
        db.DoctorQueueSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task<DoctorQueueSession> StartQueueSessionAsync(long sessionId, string doctorId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var session = await db.DoctorQueueSessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.DoctorId == doctorId, ct)
            ?? throw new KeyNotFoundException("Queue session not found.");
        if (session.Status != DoctorQueueSessionStatus.NotStarted)
            throw new InvalidOperationException("Only a not-started queue session can be started.");
        if (await db.DoctorQueueSessions.AnyAsync(s => s.DoctorId == doctorId && s.Status == DoctorQueueSessionStatus.Active, ct))
            throw new InvalidOperationException("This doctor already has an active queue session.");

        session.Status = DoctorQueueSessionStatus.Active;
        session.StartedAt = DateTimeOffset.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return session;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new InvalidOperationException("The queue session changed while it was being started. Refresh and try again.", ex);
        }
        catch (DbUpdateException ex) when (ex.GetBaseException() is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627 or 1205)
        {
            throw new InvalidOperationException("This doctor already has an active queue session. Refresh and try again.", ex);
        }
    }

    public async Task<DoctorQueueSession> CloseQueueSessionAsync(long sessionId, string doctorId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var session = await db.DoctorQueueSessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.DoctorId == doctorId, ct)
            ?? throw new KeyNotFoundException("Queue session not found.");
        if (session.Status != DoctorQueueSessionStatus.Active)
            throw new InvalidOperationException("Only an active queue session can be closed.");
        if (await db.QueueEntries.AnyAsync(q => q.DoctorId == doctorId && q.Status == QueueStatus.InConsultation, ct))
            throw new InvalidOperationException("Finish the active consultation before closing the queue session.");

        session.Status = DoctorQueueSessionStatus.Closed;
        session.ClosedAt = DateTimeOffset.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return session;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new InvalidOperationException("The queue session changed while it was being closed. Refresh and try again.", ex);
        }
    }

    public async Task<DoctorQueueSession?> GetCurrentQueueSessionAsync(string doctorId, CancellationToken ct = default)
    {
        var sessions = await GetDoctorQueueSessionsAsync(doctorId, ct);
        return sessions.FirstOrDefault(s => s.Status == DoctorQueueSessionStatus.Active)
            ?? sessions.FirstOrDefault(s => s.Status == DoctorQueueSessionStatus.NotStarted)
            ?? sessions.FirstOrDefault();
    }

    public async Task<IReadOnlyList<DoctorQueueSession>> GetDoctorQueueSessionsAsync(string doctorId, CancellationToken ct = default) =>
        await db.DoctorQueueSessions.AsNoTracking()
            .Where(s => s.DoctorId == doctorId)
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .ToListAsync(ct);

    public async Task<QueueEntry> CallNextAsync(string doctorId, CancellationToken ct = default)
    {
        await ExpireCallsAsync(doctorId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var session = await RequireActiveQueueSessionAsync(doctorId, ct);
        var now = DateTimeOffset.UtcNow;
        var active = await db.QueueEntries.AnyAsync(q => q.DoctorId == doctorId && q.Status == QueueStatus.InConsultation, ct);
        if (active) throw new InvalidOperationException("Complete the active consultation before calling the next patient.");
        if (await db.QueueEntries.AnyAsync(q => q.DoctorId == doctorId && q.Status == QueueStatus.Called, ct)) throw new InvalidOperationException("A patient has already been called.");
        var waiting = await db.QueueEntries.Include(q => q.Appointment).Where(q => q.DoctorId == doctorId && q.Status == QueueStatus.Waiting).ToListAsync(ct);
        var eligible = QueuePolicy.OrderEligible(waiting).FirstOrDefault();
        if (eligible is null) throw new InvalidOperationException("No eligible patient is waiting.");
        eligible.DoctorQueueSessionId = session.Id;
        eligible.Status = QueueStatus.Called; eligible.CalledAt = now; eligible.CallExpiresAt = now.AddMinutes(5);
        db.Events.Add(new AppointmentEvent { DoctorId = doctorId, PatientId = eligible.PatientId, AppointmentId = eligible.AppointmentId, EventType = "PatientCalled", OccurredAt = now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return eligible;
    }

    public async Task StartConsultationAsync(long queueEntryId, string doctorId, CancellationToken ct = default)
    {
        await ExpireCallsAsync(doctorId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var session = await RequireActiveQueueSessionAsync(doctorId, ct);
        if (await db.QueueEntries.AnyAsync(q => q.DoctorId == doctorId && q.Status == QueueStatus.InConsultation, ct)) throw new InvalidOperationException("A consultation is already in progress.");
        var q = await db.QueueEntries.Include(x => x.Appointment).SingleOrDefaultAsync(x => x.Id == queueEntryId && x.DoctorId == doctorId && x.Status == QueueStatus.Called, ct) ?? throw new InvalidOperationException("Only a called patient can start consultation.");
        if (q.DoctorQueueSessionId != session.Id)
            throw new InvalidOperationException("The patient was called in a different queue session.");
        if (q.CalledAt is null || q.CalledAt.Value.Add(CallTimeout) <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("The patient call has expired.");
        q.Status = QueueStatus.InConsultation; q.EnteredConsultationAt = DateTimeOffset.UtcNow;
        if (q.Appointment is { } appointment) { appointment.ConsultationStartedAt = q.EnteredConsultationAt; appointment.UpdatedAt = q.EnteredConsultationAt; }
        db.Events.Add(new AppointmentEvent { DoctorId = doctorId, PatientId = q.PatientId, AppointmentId = q.AppointmentId, EventType = "ConsultationStarted" });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task CompleteConsultationAsync(long queueEntryId, string doctorId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var session = await RequireActiveQueueSessionAsync(doctorId, ct);
        var active = await db.QueueEntries.Where(x => x.DoctorId == doctorId && x.Status == QueueStatus.InConsultation).Select(x => x.Id).SingleOrDefaultAsync(ct);
        if (active == 0 || active != queueEntryId) throw new InvalidOperationException("Only the currently active consultation can be completed.");
        var q = await db.QueueEntries.Include(x => x.Appointment).SingleOrDefaultAsync(x => x.Id == queueEntryId && x.DoctorId == doctorId && x.Status == QueueStatus.InConsultation, ct) ?? throw new InvalidOperationException("No matching consultation is in progress.");
        if (q.DoctorQueueSessionId != session.Id) throw new InvalidOperationException("The consultation belongs to a different queue session.");
        var now = DateTimeOffset.UtcNow; q.Status = QueueStatus.Completed; q.CompletedAt = now;
        if (q.Appointment is { } a) { a.ConsultationEndedAt = now; a.Status = AppointmentStatus.Completed; a.UpdatedAt = now; }
        db.Events.Add(new AppointmentEvent { DoctorId = doctorId, PatientId = q.PatientId, AppointmentId = q.AppointmentId, EventType = "ConsultationEnded", OccurredAt = now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task<QueueSnapshot> GetQueueAsync(string doctorId, CancellationToken ct = default)
    {
        await ExpireCallsAsync(doctorId, ct);
        var all = await db.QueueEntries.AsNoTracking().Include(q => q.Appointment).Where(q => q.DoctorId == doctorId).ToListAsync(ct);
        var settings = await Settings(doctorId, ct);
        var current = all.SingleOrDefault(q => q.Status == QueueStatus.InConsultation);
        var called = all.Where(q => q.Status == QueueStatus.Called).OrderBy(q => q.CalledAt).ToList();
        var waiting = all.Where(q => q.Status == QueueStatus.Waiting).ToList();
        var expectedMinutes = SchedulingRules.EffectiveSlotMinutes(settings, await DurationStats(doctorId, ct));
        var estimates = QueueEstimateCalculator.Estimate(DateTimeOffset.UtcNow, expectedMinutes, current, called, waiting);
        var occupying = all.Where(q => q.Status is QueueStatus.Waiting or QueueStatus.Called).ToList();
        var sessions = await GetDoctorQueueSessionsAsync(doctorId, ct);
        var currentSession = sessions.FirstOrDefault(s => s.Status == DoctorQueueSessionStatus.Active)
            ?? sessions.FirstOrDefault(s => s.Status == DoctorQueueSessionStatus.NotStarted)
            ?? sessions.FirstOrDefault();
        return new(doctorId, current,
            occupying.Where(q => q.Type == QueueType.Booked && q.Status == QueueStatus.Waiting).OrderBy(q => q.Appointment?.ScheduledStart).ThenBy(q => q.CheckInTime).ToList(),
            occupying.Where(q => q.Status == QueueStatus.Called).ToList(),
            occupying.Where(q => q.Type == QueueType.WalkIn && q.Status == QueueStatus.Waiting).OrderBy(q => q.ArrivalTime).ToList(),
            all.Where(q => q.Type == QueueType.WalkIn && q.Approval == WalkInApproval.Pending && q.Status == QueueStatus.Waiting).OrderBy(q => q.ArrivalTime).ToList(),
            occupying.Count, settings.WaitingRoomCapacity, settings.BookedCapacityReservation, estimates, currentSession, sessions);
    }

    public async Task ExpireCallsAsync(string doctorId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await ExpireCallsCoreAsync(doctorId, DateTimeOffset.UtcNow, ct);
        await tx.CommitAsync(ct);
    }

    private async Task ExpireCallsCoreAsync(string doctorId, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now - CallTimeout;
        var expired = await db.QueueEntries.Include(q => q.Appointment)
            .Where(q => q.DoctorId == doctorId && q.Status == QueueStatus.Called && q.CalledAt <= cutoff).ToListAsync(ct);
        if (expired.Count == 0) return;
        foreach (var q in expired)
        {
            q.Status = QueueStatus.TimedOut;
            db.Events.Add(new AppointmentEvent { DoctorId = doctorId, PatientId = q.PatientId, AppointmentId = q.AppointmentId, EventType = "PatientCallTimedOut", OccurredAt = now });
            if (q.Appointment is { Status: AppointmentStatus.Confirmed, ArrivedAt: null } a)
            {
                var settings = await Settings(doctorId, ct);
                var duration = SchedulingRules.EffectiveSlotMinutes(settings, await DurationStats(doctorId, ct));
                if (!SchedulingRules.IsWithinGrace(a.ScheduledStart, now, duration, settings.GracePeriodMultiplier))
                {
                    a.Status = AppointmentStatus.NoShow; a.NoShowAt = now;
                    db.Events.Add(new AppointmentEvent { DoctorId = doctorId, PatientId = q.PatientId, AppointmentId = a.Id, EventType = "NoShowRecorded", OccurredAt = now });
                }
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<WaitingListEntry> JoinWaitingListAsync(WaitingListEntry entry, CancellationToken ct = default)
    {
        if (entry.PreferredDateTo < entry.PreferredDateFrom) throw new InvalidOperationException("The waiting-list end date must not be before the start date.");
        if (entry.PreferredTimeFrom is { } timeFrom && entry.PreferredTimeTo is { } timeTo && timeTo < timeFrom)
            throw new InvalidOperationException("The preferred end time must not be before the start time.");
        entry.CreatedAt = DateTimeOffset.UtcNow;
        entry.Status = WaitingListStatus.Active;
        db.WaitingList.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<IReadOnlyList<AppointmentOffer>> CreateOffersForCancelledSlotAsync(Appointment appointment, int expirationMinutes = 15, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (await db.Appointments.AnyAsync(a => a.DoctorId == appointment.DoctorId && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed) && a.ScheduledStart < appointment.ScheduledEnd && a.ScheduledEnd > appointment.ScheduledStart, ct))
        { await tx.CommitAsync(ct); return []; }
        var slotDate = DateOnly.FromDateTime(appointment.ScheduledStart.UtcDateTime);
        var slotTime = TimeOnly.FromDateTime(appointment.ScheduledStart.UtcDateTime);
        var entry = await db.WaitingList.Where(w => w.Status == WaitingListStatus.Active
                && !db.Offers.Any(o => o.WaitingListEntryId == w.Id && o.SlotStart == appointment.ScheduledStart)
                && (w.PreferredDoctorId == null || w.PreferredDoctorId == appointment.DoctorId)
                && w.PreferredDateFrom <= slotDate && w.PreferredDateTo >= slotDate
                && (w.PreferredTimeFrom == null || w.PreferredTimeFrom <= slotTime)
                && (w.PreferredTimeTo == null || w.PreferredTimeTo >= slotTime))
            .OrderBy(w => w.CreatedAt).FirstOrDefaultAsync(ct);
        if (entry is null) { await tx.CommitAsync(ct); return []; }
        var offer = new AppointmentOffer { WaitingListEntryId = entry.Id, PatientId = entry.PatientId, DoctorId = appointment.DoctorId, SlotStart = appointment.ScheduledStart, SlotEnd = appointment.ScheduledEnd, SlotReleasedAt = appointment.CancelledAt ?? appointment.CreatedAt, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(expirationMinutes) };
        db.Offers.Add(offer); db.Events.Add(new AppointmentEvent { DoctorId = appointment.DoctorId, PatientId = entry.PatientId, EventType = "WaitingListOfferCreated" });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await notifier.OfferCreatedAsync(entry.PatientId, offer.Id, offer.ExpiresAt, ct);
        return [offer];
    }

    public Task AcceptOfferAsync(long offerId, string patientId, CancellationToken ct = default) => RespondOffer(offerId, patientId, true, ct);
    public Task DeclineOfferAsync(long offerId, string patientId, CancellationToken ct = default) => RespondOffer(offerId, patientId, false, ct);

    public async Task ExpireOffersAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = await db.Offers.Where(o => o.Status == OfferStatus.Pending && o.ExpiresAt <= now).ToListAsync(ct);
        foreach (var offer in expired)
        {
            offer.Status = OfferStatus.Expired; offer.RespondedAt = now; offer.Outcome = "Expired";
            db.Events.Add(new AppointmentEvent { DoctorId = offer.DoctorId, PatientId = offer.PatientId, EventType = "WaitingListOfferExpired", OccurredAt = now });
        }
        await db.SaveChangesAsync(ct);
        foreach (var offer in expired)
            await CreateOffersForCancelledSlotAsync(new Appointment { DoctorId = offer.DoctorId, ScheduledStart = offer.SlotStart, ScheduledEnd = offer.SlotEnd }, 15, ct);
    }

    public async Task ExpireAllCallsAsync(CancellationToken ct = default)
    {
        var doctors = await db.QueueEntries.AsNoTracking().Where(q => q.Status == QueueStatus.Called && q.CallExpiresAt <= DateTimeOffset.UtcNow).Select(q => q.DoctorId).Distinct().ToListAsync(ct);
        foreach (var doctorId in doctors) await ExpireCallsAsync(doctorId, ct);
    }

    private async Task RespondOffer(long id, string patient, bool accept, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var offer = await db.Offers.SingleOrDefaultAsync(o => o.Id == id && o.PatientId == patient, ct) ?? throw new KeyNotFoundException("Offer not found.");
        if (offer.Status != OfferStatus.Pending || offer.ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidOperationException("The offer is no longer active.");
        if (accept)
        {
            if (await db.Appointments.AnyAsync(a => a.DoctorId == offer.DoctorId && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed) && a.ScheduledStart < offer.SlotEnd && a.ScheduledEnd > offer.SlotStart, ct)) throw new InvalidOperationException("The offered slot has already been claimed.");
            db.Appointments.Add(new Appointment { PatientId = patient, DoctorId = offer.DoctorId, ScheduledStart = offer.SlotStart, ScheduledEnd = offer.SlotEnd, Status = AppointmentStatus.Confirmed, ConfirmedAt = DateTimeOffset.UtcNow });
            offer.Status = OfferStatus.Accepted; offer.Outcome = "Claimed";
            var entry = await db.WaitingList.SingleAsync(x => x.Id == offer.WaitingListEntryId, ct);
            entry.Status = WaitingListStatus.Matched;
        }
        else { offer.Status = OfferStatus.Declined; offer.Outcome = "Declined"; }
        offer.RespondedAt = DateTimeOffset.UtcNow; db.Events.Add(new AppointmentEvent { DoctorId = offer.DoctorId, PatientId = patient, EventType = accept ? "WaitingListOfferAccepted" : "WaitingListOfferDeclined" });
        if (accept)
        {
            var recoveryMinutes = (DateTimeOffset.UtcNow - offer.SlotReleasedAt).TotalMinutes;
            db.Events.Add(new AppointmentEvent { DoctorId = offer.DoctorId, PatientId = patient, EventType = "CancelledSlotRecovered", OccurredAt = DateTimeOffset.UtcNow, Details = $"Offer {offer.Id}; recoveryMinutes={Math.Max(0, recoveryMinutes):F1}" });
        }
        var nextSlot = new Appointment { DoctorId = offer.DoctorId, ScheduledStart = offer.SlotStart, ScheduledEnd = offer.SlotEnd };
        if (!accept) { var entry = await db.WaitingList.SingleAsync(x => x.Id == offer.WaitingListEntryId, ct); entry.Status = WaitingListStatus.Active; }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        if (!accept) await CreateOffersForCancelledSlotAsync(nextSlot, 15, ct);
    }

    private Task<DoctorSchedulingSettings> Settings(string doctorId, CancellationToken ct) => db.SchedulingSettings.SingleOrDefaultAsync(x => x.DoctorId == doctorId, ct).ContinueWith(t => t.Result ?? throw new InvalidOperationException("Doctor scheduling settings are not configured."), ct);
    private async Task<ConsultationStatistics> DurationStats(string doctorId, CancellationToken ct)
    {
        var rows = await db.Appointments.Where(a => a.DoctorId == doctorId && a.Status == AppointmentStatus.Completed && a.ConsultationStartedAt != null && a.ConsultationEndedAt > a.ConsultationStartedAt).OrderByDescending(a => a.ConsultationEndedAt).Take(30).Select(a => new { a.ConsultationStartedAt, a.ConsultationEndedAt }).ToListAsync(ct);
        return ConsultationStatistics.Calculate(rows.Select(x => x.ConsultationEndedAt!.Value - x.ConsultationStartedAt!.Value));
    }
    private async Task EnsureSeatAvailable(string doctorId, DoctorSchedulingSettings settings, bool booked, CancellationToken ct)
    {
        var occupancy = await db.QueueEntries.CountAsync(q => q.DoctorId == doctorId && (q.Status == QueueStatus.Waiting || q.Status == QueueStatus.Called), ct);
        if (occupancy >= settings.WaitingRoomCapacity) throw new InvalidOperationException("The waiting room is full.");
        if (!booked)
        {
            var unarrived = await db.Appointments.CountAsync(a => a.DoctorId == doctorId && a.Status == AppointmentStatus.Confirmed && a.CheckedInAt == null && a.ScheduledStart > DateTimeOffset.UtcNow, ct);
            if (settings.WaitingRoomCapacity - occupancy <= Math.Min(settings.BookedCapacityReservation, unarrived)) throw new InvalidOperationException("Capacity is reserved for booked patients.");
        }
    }

    private async Task<DoctorQueueSession> RequireActiveQueueSessionAsync(string doctorId, CancellationToken ct)
    {
        return await db.DoctorQueueSessions.SingleOrDefaultAsync(s => s.DoctorId == doctorId && s.Status == DoctorQueueSessionStatus.Active, ct)
            ?? throw new InvalidOperationException("Start an active queue session before operating the queue.");
    }
}

public sealed class LocalAppointmentNotifier : IAppointmentNotifier
{
    private readonly AppointmentDbContext _db;
    public LocalAppointmentNotifier(AppointmentDbContext db) => _db = db;

    public async Task OfferCreatedAsync(string patientId, long offerId, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        _db.AppointmentNotifications.Add(new AppointmentNotification
        {
            PatientId = patientId,
            OfferId = offerId,
            Type = "AppointmentOffer",
            Message = $"An earlier appointment is available. Respond before {expiresAt:u}."
        });
        await _db.SaveChangesAsync(ct);
    }
}
