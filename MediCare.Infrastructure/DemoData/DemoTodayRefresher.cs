using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediCare.Infrastructure.DemoData;

/// <summary>
/// Puts a live queue on today's board for every demo doctor who works today: checked-in patients, an approved walk-in
/// waiting, a walk-in waiting for the doctor's approval, and one patient in consultation. Run it on the day you want to
/// show the queue. Doctors that already have three or more live entries today are left alone.
/// </summary>
public static class DemoTodayRefresher
{
    public static async Task<DemoSeedResult> RefreshAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClinicClock>();
        var rng = new Random();
        var now = clock.UtcNow;
        var today = clock.Today;

        var doctorEmails = DemoDataCatalog.Doctors.Where(item => item.Active).Select(item => item.Email).ToArray();
        var demoDoctors = await (
                from doctor in db.Doctors
                join user in db.Users on doctor.ApplicationUserId equals user.Id
                where doctor.IsActive && doctorEmails.Contains(user.Email!)
                select new { Doctor = doctor, UserId = user.Id })
            .ToListAsync(cancellationToken);
        if (demoDoctors.Count == 0)
            return new DemoSeedResult(false, "No demo doctors found. Run with --seed-demo first.");

        var receptionIds = await db.Users
            .Where(user => user.Email == DemoDataSeeder.MarkerEmail || user.Email == "reception2.demo@medicare.test")
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

        var workingToday = await db.DoctorWorkingHours
            .Where(item => item.IsActive && item.DayOfWeek == today.DayOfWeek)
            .Select(item => item.DoctorId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var onHoliday = await db.DoctorHolidays
            .Where(item => item.StartDate <= today && item.EndDate >= today)
            .Select(item => item.DoctorId)
            .ToListAsync(cancellationToken);

        // Patients with a booking or a live attendance today are not used. Patients whose visits today are already
        // finished come after patients who have not been to the clinic today.
        var liveToday = (await db.Appointments
                .Where(item => item.AppointmentDate == today && (item.Status == AppointmentStatus.Pending || item.Status == AppointmentStatus.Confirmed))
                .Select(item => item.PatientId)
                .ToListAsync(cancellationToken))
            .Concat(await db.QueueEntries
                .Where(item => item.QueueDate == today && (item.Status == QueueEntryStatus.Waiting || item.Status == QueueEntryStatus.Called || item.Status == QueueEntryStatus.InConsultation))
                .Select(item => item.PatientId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var visitedToday = (await db.QueueEntries.Where(item => item.QueueDate == today).Select(item => item.PatientId).ToListAsync(cancellationToken)).ToHashSet();
        var freePatients = (await db.Patients.Where(item => item.PhoneNumber.StartsWith("+2010090000")).ToListAsync(cancellationToken))
            .Where(item => !liveToday.Contains(item.Id))
            .OrderBy(item => visitedToday.Contains(item.Id) ? 1 : 0)
            .ThenBy(_ => rng.Next())
            .ToQueue();

        var refreshed = new List<string>();
        foreach (var demo in demoDoctors.Where(item => workingToday.Contains(item.Doctor.Id) && !onHoliday.Contains(item.Doctor.Id)))
        {
            var doctor = demo.Doctor;
            var todayEntries = await db.QueueEntries.Where(item => item.DoctorId == doctor.Id && item.QueueDate == today).ToListAsync(cancellationToken);
            var todayEntryIds = todayEntries.Select(item => item.Id).ToList();
            var todayApprovals = await db.WalkInAdmissionApprovals.Where(item => todayEntryIds.Contains(item.QueueEntryId)).ToListAsync(cancellationToken);

            var sequence = todayEntries.Where(item => item.QueueSequence != null).Select(item => item.QueueSequence!.Value).DefaultIfEmpty(0).Max();
            var hasConsultation = await db.QueueEntries.AnyAsync(item => item.DoctorId == doctor.Id && item.Status == QueueEntryStatus.InConsultation, cancellationToken);
            var liveAppointmentIds = todayEntries.Where(item => item.Status.IsLive() && item.AppointmentId != null).Select(item => item.AppointmentId!.Value).ToHashSet();
            var waitingScheduled = todayEntries.Count(item => item.Source == QueueEntrySource.Scheduled && item.Status == QueueEntryStatus.Waiting);
            var hasApprovedWaiting = todayApprovals.Any(approval => approval.Decision == WalkInAdmissionDecision.Approved && approval.UsedAtUtc == null
                && todayEntries.Any(entry => entry.Id == approval.QueueEntryId && entry.Status == QueueEntryStatus.Waiting));
            var hasPendingRequest = todayApprovals.Any(approval => approval.Decision == WalkInAdmissionDecision.Pending && approval.ExpiresAtUtc > now);
            if (hasConsultation && waitingScheduled >= 2 && hasApprovedWaiting && hasPendingRequest)
                continue;

            // Check in the next confirmed patients of the day.
            var upcoming = await db.Appointments
                .Where(item => item.DoctorId == doctor.Id && item.AppointmentDate == today && item.Status == AppointmentStatus.Confirmed)
                .OrderBy(item => item.StartTime)
                .ToListAsync(cancellationToken);
            foreach (var appointment in upcoming.Where(item => !liveAppointmentIds.Contains(item.Id) && clock.ToUtc(today, item.StartTime) >= now.AddMinutes(-20)).Take(Math.Max(0, 2 - waitingScheduled)))
                db.QueueEntries.Add(QueueEntry.CreateScheduled(appointment.PatientId, doctor.Id, appointment.Id, today, now.AddMinutes(-rng.Next(4, 18))));

            if (!hasConsultation && freePatients.TryDequeue(out var inRoom))
            {
                var entry = AddWalkIn(db, inRoom, doctor, today, ++sequence, now.AddMinutes(-28), now, out var approval);
                approval.Approve(now.AddMinutes(-24));
                entry.CallWalkIn(approval, now.AddMinutes(-9), today);
                entry.StartConsultation(now.AddMinutes(-7));
            }

            if (!hasApprovedWaiting && freePatients.TryDequeue(out var approvedPatient))
            {
                AddWalkIn(db, approvedPatient, doctor, today, ++sequence, now.AddMinutes(-16), now, out var approval);
                approval.Approve(now.AddMinutes(-12));
                foreach (var receptionId in receptionIds)
                    db.Notifications.Add(Notification.Create(receptionId, StaffNotificationKind.WalkInAdmissionDecided.ToString(), "Walk-in admission approved",
                        $"{doctor.FullName} approved the walk-in at queue position {sequence}. You can call the patient from the Queue page.",
                        "WalkInAdmissionApproval", approval.Id, null, now.AddMinutes(-12)));
            }

            if (!hasPendingRequest && freePatients.TryDequeue(out var pendingPatient))
            {
                AddWalkIn(db, pendingPatient, doctor, today, ++sequence, now.AddMinutes(-4), now, out var approval);
                db.Notifications.Add(Notification.Create(demo.UserId, StaffNotificationKind.WalkInAdmissionRequested.ToString(), "Walk-in admission requested",
                    $"Reception requested admission approval for walk-in queue position {sequence}. Review it on the Walk-in admissions page.",
                    "WalkInAdmissionApproval", approval.Id, null, approval.RequestedAtUtc));
            }

            refreshed.Add(doctor.FullName);
        }

        await db.SaveChangesAsync(cancellationToken);
        return refreshed.Count == 0
            ? new DemoSeedResult(false, $"No demo doctor needed a live queue on {today:yyyy-MM-dd} (nobody works today, or the queues are already busy).")
            : new DemoSeedResult(true, $"Live queue added for {today:yyyy-MM-dd}: {string.Join(", ", refreshed)}.");
    }

    private static QueueEntry AddWalkIn(MediCareDbContext db, Patient patient, Doctor doctor, DateOnly today, long sequence,
        DateTimeOffset arrivedAt, DateTimeOffset now, out WalkInAdmissionApproval approval)
    {
        var entry = QueueEntry.CreateWalkIn(patient.Id, doctor.Id, today, arrivedAt, sequence);
        db.QueueEntries.Add(entry);
        var requested = arrivedAt.AddMinutes(1) <= now ? arrivedAt.AddMinutes(1) : arrivedAt;
        approval = WalkInAdmissionApproval.Request(entry, requested, requested.AddMinutes(30));
        db.WalkInAdmissionApprovals.Add(approval);
        return entry;
    }

    private static Queue<T> ToQueue<T>(this IEnumerable<T> items) => new(items);
}
