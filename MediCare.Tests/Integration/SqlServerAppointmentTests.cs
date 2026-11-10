using MediCare.Domain;
using MediCare.Application;
using MediCare.Infrastructure.Persistence;
using MediCare.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Tests.Integration;

public sealed class SqlServerAppointmentTests
{
    [Fact]
    public async Task Booking_is_persisted_and_overlapping_booking_is_rejected()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, date) = await SeedDoctor(db);
            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            var start = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc));
            var booked = await service.BookAsync("patient-a", doctor, start);

            Assert.Equal(AppointmentStatus.Confirmed, booked.Status);
            Assert.Equal(start, (await db.Appointments.SingleAsync()).ScheduledStart);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.BookAsync("patient-b", doctor, start.AddMinutes(5)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.BookAsync("patient-past", doctor, DateTimeOffset.UtcNow.AddMinutes(-1)));
            Assert.Single(await db.Appointments.ToListAsync());
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task Booking_rejects_outside_hours_breaks_and_approved_leave()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, date) = await SeedDoctor(db);
            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            var outside = new DateTimeOffset(date.ToDateTime(new TimeOnly(19, 0), DateTimeKind.Utc));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.BookAsync("patient", doctor, outside));

            var start = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc));
            db.Breaks.Add(new DoctorBreak { DoctorId = doctor, StartsAt = start, EndsAt = start.AddHours(1) });
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.BookAsync("patient", doctor, start));

            db.Breaks.RemoveRange(await db.Breaks.Where(x => x.DoctorId == doctor).ToListAsync());
            db.Leaves.Add(new DoctorLeave { DoctorId = doctor, StartsAt = start, EndsAt = start.AddHours(1), Reason = "Test leave", IsApproved = true });
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.BookAsync("patient", doctor, start));
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task Doctor_search_uses_public_profile_fields_and_does_not_expose_email()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var doctorId = Guid.NewGuid().ToString();
            var doctorRole = new IdentityRole { Id = Guid.NewGuid().ToString(), Name = "Doctor", NormalizedName = "DOCTOR" };
            db.Roles.Add(doctorRole);
            db.Users.Add(new IdentityUser { Id = doctorId, UserName = "private-doctor@example.test", NormalizedUserName = "PRIVATE-DOCTOR@EXAMPLE.TEST", Email = "private-doctor@example.test", NormalizedEmail = "PRIVATE-DOCTOR@EXAMPLE.TEST" });
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = doctorId, RoleId = doctorRole.Id });
            db.SchedulingSettings.Add(new DoctorSchedulingSettings { DoctorId = doctorId, PublicDisplayName = "Dr. Example", Specialization = "Cardiology", ConsultationFee = 500, DefaultConsultationDurationMinutes = 20 });
            await db.SaveChangesAsync();

            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            var result = await service.SearchDoctorsAsync("Cardio");

            var doctor = Assert.Single(result);
            Assert.Equal("Dr. Example", doctor.DisplayName);
            Assert.Equal("Cardiology", doctor.Specialization);
            Assert.Equal(500, doctor.ConsultationFee);
            Assert.DoesNotContain("@", doctor.DisplayName);
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task Concurrent_offer_acceptance_can_claim_slot_only_once()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, date) = await SeedDoctor(db);
            var patient = "patient-waiting";
            db.WaitingList.Add(new WaitingListEntry
            {
                PatientId = patient, PreferredDoctorId = doctor,
                PreferredDateFrom = date, PreferredDateTo = date,
                Status = WaitingListStatus.Active
            });
            await db.SaveChangesAsync();

            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            var booked = await service.BookAsync("original-patient", doctor, new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc)));
            await service.CancelAsync(booked.Id, "original-patient", "Test recovery");
            var offerId = await db.Offers.Select(x => x.Id).SingleAsync();
            Assert.Single(await db.AppointmentNotifications.AsNoTracking().ToListAsync());

            async Task<bool> TryAccept()
            {
                try
                {
                    await using var contenderDb = new AppointmentDbContext(options);
                    var contender = new AppointmentService(contenderDb, new LocalAppointmentNotifier(contenderDb));
                    await contender.AcceptOfferAsync(offerId, patient);
                    return true;
                }
                catch (Exception) { return false; }
            }

            var results = await Task.WhenAll(TryAccept(), TryAccept());
            Assert.Single(results, x => x);
            Assert.Single(await db.Appointments.ToListAsync(), x => x.Status == AppointmentStatus.Confirmed);
            Assert.Equal(OfferStatus.Accepted, (await db.Offers.AsNoTracking().SingleAsync()).Status);
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task Doctor_can_call_another_patient_after_completing_the_current_consultation()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, _) = await SeedDoctor(db);
            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            await ActivateQueueSession(service, doctor);
            var first = await service.RegisterWalkInAsync(doctor, "walk-in-a", DateTimeOffset.UtcNow);
            await service.ApproveWalkInAsync(first.Id, doctor, approve: true, defer: false);
            var second = await service.RegisterWalkInAsync(doctor, "walk-in-b", DateTimeOffset.UtcNow.AddMinutes(1));
            await service.ApproveWalkInAsync(second.Id, doctor, approve: true, defer: false);

            var calledFirst = await service.CallNextAsync(doctor);
            await service.StartConsultationAsync(calledFirst.Id, doctor);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CallNextAsync(doctor));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartConsultationAsync(second.Id, doctor));
            await service.CompleteConsultationAsync(calledFirst.Id, doctor);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteConsultationAsync(second.Id, doctor));

            var calledSecond = await service.CallNextAsync(doctor);
            Assert.Equal(second.Id, calledSecond.Id);
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task Cancelled_slot_skips_older_waitlist_entry_outside_preferred_time()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, date) = await SeedDoctor(db);
            var start = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc));
            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.JoinWaitingListAsync(new WaitingListEntry
            {
                PatientId = "invalid-date", PreferredDateFrom = date.AddDays(1), PreferredDateTo = date
            }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.JoinWaitingListAsync(new WaitingListEntry
            {
                PatientId = "invalid-time", PreferredDateFrom = date, PreferredDateTo = date,
                PreferredTimeFrom = new TimeOnly(12, 0), PreferredTimeTo = new TimeOnly(11, 0)
            }));
            await service.JoinWaitingListAsync(new WaitingListEntry
            {
                PatientId = "early-window", PreferredDoctorId = doctor,
                PreferredDateFrom = date, PreferredDateTo = date,
                PreferredTimeFrom = new TimeOnly(8, 0), PreferredTimeTo = new TimeOnly(9, 0)
            });
            await service.JoinWaitingListAsync(new WaitingListEntry
            {
                PatientId = "matching-window", PreferredDoctorId = doctor,
                PreferredDateFrom = date, PreferredDateTo = date,
                PreferredTimeFrom = new TimeOnly(9, 0), PreferredTimeTo = new TimeOnly(11, 0)
            });

            var appointment = await service.BookAsync("original", doctor, start);
            await service.CancelAsync(appointment.Id, "original", "Test recovery");

            var offer = await db.Offers.SingleAsync();
            Assert.Equal("matching-window", offer.PatientId);
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task On_time_checkin_flows_through_call_start_and_completion()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, date) = await SeedDoctor(db);
            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            var start = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc));
            var appointment = await service.BookAsync("patient-flow", doctor, start);
            await service.CheckInAsync(appointment.Id, "patient-flow", start.AddMinutes(40));
            Assert.Empty(await db.DoctorQueueSessions.ToListAsync());

            var queueEntry = await db.QueueEntries.SingleAsync();
            Assert.Equal(QueueType.Booked, queueEntry.Type);
            Assert.Equal(WalkInApproval.Approved, queueEntry.Approval);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelAsync(appointment.Id, "patient-flow", "Already checked in"));
            await ActivateQueueSession(service, doctor);
            var called = await service.CallNextAsync(doctor);
            await service.StartConsultationAsync(called.Id, doctor);
            await service.CompleteConsultationAsync(called.Id, doctor);

            Assert.Equal(AppointmentStatus.Completed, (await db.Appointments.SingleAsync()).Status);
            Assert.NotNull((await db.Appointments.SingleAsync()).ConsultationStartedAt);
            Assert.NotNull((await db.Appointments.SingleAsync()).ConsultationEndedAt);
            Assert.Equal(QueueStatus.Completed, (await db.QueueEntries.SingleAsync()).Status);
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task Late_checkin_requires_doctor_approval_and_call_timeout_is_recorded()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, date) = await SeedDoctor(db);
            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            var start = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc));
            var appointment = await service.BookAsync("late-patient", doctor, start);
            await service.CheckInAsync(appointment.Id, "late-patient", start.AddMinutes(41));

            var entry = await db.QueueEntries.SingleAsync();
            Assert.Equal(QueueType.WalkIn, entry.Type);
            Assert.Equal(WalkInApproval.Pending, entry.Approval);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CallNextAsync(doctor));
            await ActivateQueueSession(service, doctor);
            await service.ApproveWalkInAsync(entry.Id, doctor, approve: true, defer: false);
            var called = await service.CallNextAsync(doctor);
            called.CalledAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
            await service.ExpireCallsAsync(doctor);

            Assert.Equal(QueueStatus.TimedOut, (await db.QueueEntries.SingleAsync()).Status);
            Assert.Contains(await db.Events.Select(e => e.EventType).ToListAsync(), e => e == "PatientCallTimedOut");
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task CallNext_expires_boundary_call_before_selecting_and_expiration_is_idempotent()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, _) = await SeedDoctor(db);
            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            await ActivateQueueSession(service, doctor);
            var scheduled = DateTimeOffset.UtcNow.AddHours(-2);
            var attended = new Appointment { DoctorId = doctor, PatientId = "attended", ScheduledStart = scheduled, ScheduledEnd = scheduled.AddMinutes(20), Status = AppointmentStatus.Confirmed, ArrivedAt = scheduled.AddMinutes(30), CheckedInAt = scheduled.AddMinutes(30) };
            db.Appointments.Add(attended);
            await db.SaveChangesAsync();
            var expired = new QueueEntry { DoctorId = doctor, PatientId = "expired", Type = QueueType.WalkIn, Approval = WalkInApproval.Approved, Status = QueueStatus.Called, ArrivalTime = DateTimeOffset.UtcNow.AddMinutes(-5), CalledAt = DateTimeOffset.UtcNow.AddMinutes(-5), CallExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
            var attendedCall = new QueueEntry { DoctorId = doctor, PatientId = attended.PatientId, AppointmentId = attended.Id, Type = QueueType.Booked, Approval = WalkInApproval.Approved, Status = QueueStatus.Called, ArrivalTime = attended.ArrivedAt!.Value, CheckInTime = attended.CheckedInAt, CalledAt = DateTimeOffset.UtcNow.AddMinutes(-5), CallExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
            var waiting = new QueueEntry { DoctorId = doctor, PatientId = "waiting", Type = QueueType.WalkIn, Approval = WalkInApproval.Approved, Status = QueueStatus.Waiting, ArrivalTime = DateTimeOffset.UtcNow };
            db.QueueEntries.AddRange(expired, attendedCall, waiting);
            await db.SaveChangesAsync();
            var next = await service.CallNextAsync(doctor);
            Assert.Equal(waiting.Id, next.Id);
            Assert.Equal(QueueStatus.TimedOut, (await db.QueueEntries.SingleAsync(q => q.Id == expired.Id)).Status);
            Assert.Equal(QueueStatus.TimedOut, (await db.QueueEntries.SingleAsync(q => q.Id == attendedCall.Id)).Status);
            Assert.Equal(AppointmentStatus.Confirmed, (await db.Appointments.SingleAsync(a => a.Id == attended.Id)).Status);
            await service.ExpireCallsAsync(doctor);
            Assert.Equal(2, await db.Events.CountAsync(e => e.EventType == "PatientCallTimedOut"));
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task Concurrent_call_requests_create_at_most_one_active_call()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, _) = await SeedDoctor(db);
            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            await ActivateQueueSession(service, doctor);
            db.QueueEntries.AddRange(
                new QueueEntry { DoctorId = doctor, PatientId = "one", Type = QueueType.WalkIn, Approval = WalkInApproval.Approved, Status = QueueStatus.Waiting, ArrivalTime = DateTimeOffset.UtcNow },
                new QueueEntry { DoctorId = doctor, PatientId = "two", Type = QueueType.WalkIn, Approval = WalkInApproval.Approved, Status = QueueStatus.Waiting, ArrivalTime = DateTimeOffset.UtcNow.AddTicks(1) });
            await db.SaveChangesAsync();
            async Task<bool> TryCall()
            {
                try
                {
                    await using var contenderDb = new AppointmentDbContext(options);
                    await new AppointmentService(contenderDb, new LocalAppointmentNotifier(contenderDb)).CallNextAsync(doctor);
                    return true;
                }
                catch (Exception) { return false; }
            }

            var results = await Task.WhenAll(TryCall(), TryCall());
            Assert.Single(results, success => success);
            Assert.Single(await db.QueueEntries.AsNoTracking().Where(q => q.DoctorId == doctor && q.Status == QueueStatus.Called).ToListAsync());
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task Concurrent_start_requests_create_at_most_one_active_consultation()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, _) = await SeedDoctor(db);
            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            var queueSession = await service.CreateQueueSessionAsync(doctor);
            await service.StartQueueSessionAsync(queueSession.Id, doctor);
            var first = new QueueEntry { DoctorId = doctor, PatientId = "one", Type = QueueType.WalkIn, Approval = WalkInApproval.Approved, Status = QueueStatus.Called, ArrivalTime = DateTimeOffset.UtcNow, CalledAt = DateTimeOffset.UtcNow, CallExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) };
            var second = new QueueEntry { DoctorId = doctor, PatientId = "two", Type = QueueType.WalkIn, Approval = WalkInApproval.Approved, Status = QueueStatus.Called, ArrivalTime = DateTimeOffset.UtcNow, CalledAt = DateTimeOffset.UtcNow, CallExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) };
            first.DoctorQueueSessionId = queueSession.Id;
            second.DoctorQueueSessionId = queueSession.Id;
            db.QueueEntries.AddRange(first, second);
            await db.SaveChangesAsync();
            async Task<bool> TryStart(long id)
            {
                try
                {
                    await using var contenderDb = new AppointmentDbContext(options);
                    await new AppointmentService(contenderDb, new LocalAppointmentNotifier(contenderDb)).StartConsultationAsync(id, doctor);
                    return true;
                }
                catch (Exception) { return false; }
            }

            var results = await Task.WhenAll(TryStart(first.Id), TryStart(second.Id));
            Assert.Single(results, success => success);
            Assert.Single(await db.QueueEntries.AsNoTracking().Where(q => q.DoctorId == doctor && q.Status == QueueStatus.InConsultation).ToListAsync());
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task Queue_session_lifecycle_is_manual_and_keeps_same_day_history()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, _) = await SeedDoctor(db);
            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            var first = await service.CreateQueueSessionAsync(doctor);
            Assert.Equal(DoctorQueueSessionStatus.NotStarted, first.Status);
            Assert.Null(first.StartedAt);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CloseQueueSessionAsync(first.Id, doctor));

            var started = await service.StartQueueSessionAsync(first.Id, doctor);
            Assert.Equal(DoctorQueueSessionStatus.Active, started.Status);
            Assert.NotNull(started.StartedAt);
            var startedAt = started.StartedAt;
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartQueueSessionAsync(first.Id, doctor));
            Assert.Equal(startedAt, (await db.DoctorQueueSessions.SingleAsync(s => s.Id == first.Id)).StartedAt);

            var closed = await service.CloseQueueSessionAsync(first.Id, doctor);
            Assert.Equal(DoctorQueueSessionStatus.Closed, closed.Status);
            Assert.NotNull(closed.ClosedAt);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CloseQueueSessionAsync(first.Id, doctor));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartQueueSessionAsync(first.Id, doctor));

            var second = await service.CreateQueueSessionAsync(doctor);
            Assert.Equal(first.SessionDate, second.SessionDate);
            await service.StartQueueSessionAsync(second.Id, doctor);
            await service.CloseQueueSessionAsync(second.Id, doctor);
            Assert.Equal(2, await db.DoctorQueueSessions.CountAsync(s => s.DoctorId == doctor && s.SessionDate == first.SessionDate));
            Assert.Equal(2, await db.DoctorQueueSessions.CountAsync(s => s.DoctorId == doctor && s.Status == DoctorQueueSessionStatus.Closed));
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task Queue_operations_require_active_owned_session_and_session_close_is_manual()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, _) = await SeedDoctor(db);
            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            var waiting = new QueueEntry { DoctorId = doctor, PatientId = "waiting", Type = QueueType.WalkIn, Approval = WalkInApproval.Approved, Status = QueueStatus.Waiting, ArrivalTime = DateTimeOffset.UtcNow };
            db.QueueEntries.Add(waiting);
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CallNextAsync(doctor));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartConsultationAsync(waiting.Id, doctor));

            var session = await service.CreateQueueSessionAsync(doctor);
            await service.StartQueueSessionAsync(session.Id, doctor);
            var called = await service.CallNextAsync(doctor);
            await service.StartConsultationAsync(called.Id, doctor);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CloseQueueSessionAsync(session.Id, doctor));
            await service.CompleteConsultationAsync(called.Id, doctor);
            Assert.Equal(DoctorQueueSessionStatus.Active, (await db.DoctorQueueSessions.SingleAsync(s => s.Id == session.Id)).Status);

            var remaining = new QueueEntry { DoctorId = doctor, PatientId = "remaining", Type = QueueType.WalkIn, Approval = WalkInApproval.Approved, Status = QueueStatus.Waiting, ArrivalTime = DateTimeOffset.UtcNow };
            var staysWaiting = new QueueEntry { DoctorId = doctor, PatientId = "stays-waiting", Type = QueueType.WalkIn, Approval = WalkInApproval.Approved, Status = QueueStatus.Waiting, ArrivalTime = DateTimeOffset.UtcNow.AddTicks(1) };
            db.QueueEntries.AddRange(remaining, staysWaiting);
            await db.SaveChangesAsync();
            var calledInFirstSession = await service.CallNextAsync(doctor);
            Assert.Equal(remaining.Id, calledInFirstSession.Id);
            await service.CloseQueueSessionAsync(session.Id, doctor);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CallNextAsync(doctor));
            Assert.Equal(QueueStatus.Waiting, (await db.QueueEntries.SingleAsync(q => q.Id == staysWaiting.Id)).Status);
            var nextSession = await service.CreateQueueSessionAsync(doctor);
            await service.StartQueueSessionAsync(nextSession.Id, doctor);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartConsultationAsync(calledInFirstSession.Id, doctor));
            var recalled = await service.CallNextAsync(doctor);
            Assert.Equal(calledInFirstSession.Id, recalled.Id);
            Assert.Equal(nextSession.Id, recalled.DoctorQueueSessionId);
            await service.StartConsultationAsync(recalled.Id, doctor);
            Assert.Equal(DoctorQueueSessionStatus.Closed, (await db.DoctorQueueSessions.SingleAsync(s => s.Id == session.Id)).Status);
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task Booking_uses_approved_duration_and_persists_duration_snapshot_despite_long_history()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, date) = await SeedDoctor(db);
            var settings = await db.SchedulingSettings.SingleAsync(s => s.DoctorId == doctor);
            settings.DefaultConsultationDurationMinutes = 15;
            settings.MinimumHistorySamples = 2;
            await db.SaveChangesAsync();
            for (var i = 0; i < 5; i++)
            {
                var start = DateTimeOffset.UtcNow.AddDays(-i - 1);
                db.Appointments.Add(new Appointment { DoctorId = doctor, PatientId = $"history-{i}", ScheduledStart = start, ScheduledEnd = start.AddMinutes(80), DurationSnapshotMinutes = 80, Status = AppointmentStatus.Completed, ConsultationStartedAt = start, ConsultationEndedAt = start.AddMinutes(80) });
            }
            await db.SaveChangesAsync();
            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            var requested = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc));
            var appointment = await service.BookAsync("snapshot-patient", doctor, requested);
            Assert.Equal(15, appointment.DurationSnapshotMinutes);
            Assert.Equal(requested.AddMinutes(15), appointment.ScheduledEnd);
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task Doctor_cannot_start_close_or_operate_another_doctors_session_or_queue()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctorA, _) = await SeedDoctor(db);
            var (doctorB, _) = await SeedDoctor(db);
            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            var sessionB = await service.CreateQueueSessionAsync(doctorB);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.StartQueueSessionAsync(sessionB.Id, doctorA));
            await service.StartQueueSessionAsync(sessionB.Id, doctorB);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CloseQueueSessionAsync(sessionB.Id, doctorA));

            var calledB = new QueueEntry { DoctorId = doctorB, PatientId = "patient-b", DoctorQueueSessionId = sessionB.Id, Type = QueueType.WalkIn, Approval = WalkInApproval.Approved, Status = QueueStatus.Called, ArrivalTime = DateTimeOffset.UtcNow, CalledAt = DateTimeOffset.UtcNow, CallExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) };
            db.QueueEntries.Add(calledB);
            await db.SaveChangesAsync();
            var sessionA = await service.CreateQueueSessionAsync(doctorA);
            await service.StartQueueSessionAsync(sessionA.Id, doctorA);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartConsultationAsync(calledB.Id, doctorA));
            Assert.Equal(QueueStatus.Called, (await db.QueueEntries.SingleAsync(q => q.Id == calledB.Id)).Status);
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task Concurrent_session_starts_and_close_start_race_preserve_session_invariants()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, _) = await SeedDoctor(db);
            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            var first = await service.CreateQueueSessionAsync(doctor);
            var second = await service.CreateQueueSessionAsync(doctor);
            async Task<bool> TryStartSession(long id)
            {
                try
                {
                    await using var contenderDb = new AppointmentDbContext(options);
                    await new AppointmentService(contenderDb, new LocalAppointmentNotifier(contenderDb)).StartQueueSessionAsync(id, doctor);
                    return true;
                }
                catch (Exception) { return false; }
            }
            var sessionResults = await Task.WhenAll(TryStartSession(first.Id), TryStartSession(second.Id));
            Assert.Single(sessionResults, success => success);
            var activeSession = await db.DoctorQueueSessions.AsNoTracking().SingleAsync(s => s.DoctorId == doctor && s.Status == DoctorQueueSessionStatus.Active);

            var called = new QueueEntry { DoctorId = doctor, PatientId = "race", DoctorQueueSessionId = activeSession.Id, Type = QueueType.WalkIn, Approval = WalkInApproval.Approved, Status = QueueStatus.Called, ArrivalTime = DateTimeOffset.UtcNow, CalledAt = DateTimeOffset.UtcNow, CallExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) };
            db.QueueEntries.Add(called);
            await db.SaveChangesAsync();
            async Task<bool> TryClose()
            {
                try
                {
                    await using var contenderDb = new AppointmentDbContext(options);
                    await new AppointmentService(contenderDb, new LocalAppointmentNotifier(contenderDb)).CloseQueueSessionAsync(activeSession.Id, doctor);
                    return true;
                }
                catch (Exception) { return false; }
            }
            async Task<bool> TryStartConsultation()
            {
                try
                {
                    await using var contenderDb = new AppointmentDbContext(options);
                    await new AppointmentService(contenderDb, new LocalAppointmentNotifier(contenderDb)).StartConsultationAsync(called.Id, doctor);
                    return true;
                }
                catch (Exception) { return false; }
            }
            var closeStartResults = await Task.WhenAll(TryClose(), TryStartConsultation());
            Assert.Single(closeStartResults, succeeded => succeeded);
            var finalSession = await db.DoctorQueueSessions.AsNoTracking().SingleAsync(s => s.Id == activeSession.Id);
            var consultationExists = await db.QueueEntries.AsNoTracking().AnyAsync(q => q.Id == called.Id && q.Status == QueueStatus.InConsultation);
            Assert.False(finalSession.Status == DoctorQueueSessionStatus.Closed && consultationExists);
            Assert.Equal(closeStartResults[0], finalSession.Status == DoctorQueueSessionStatus.Closed);
            Assert.Equal(closeStartResults[1], consultationExists);
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task Database_prevents_more_than_one_active_session_per_doctor()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, _) = await SeedDoctor(db);
            var now = DateTimeOffset.UtcNow;
            db.DoctorQueueSessions.Add(new DoctorQueueSession { DoctorId = doctor, SessionDate = DateOnly.FromDateTime(now.UtcDateTime), Status = DoctorQueueSessionStatus.Active, StartedAt = now });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            db.DoctorQueueSessions.AddRange(
                new DoctorQueueSession { DoctorId = doctor, SessionDate = DateOnly.FromDateTime(now.UtcDateTime), Status = DoctorQueueSessionStatus.Active, StartedAt = now },
                new DoctorQueueSession { DoctorId = doctor, SessionDate = DateOnly.FromDateTime(now.UtcDateTime), Status = DoctorQueueSessionStatus.Active, StartedAt = now });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task Legacy_in_consultation_entry_is_attached_and_can_be_completed_after_session_start()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, _) = await SeedDoctor(db);
            var legacy = new QueueEntry { DoctorId = doctor, PatientId = "legacy-patient", Type = QueueType.Booked, Status = QueueStatus.InConsultation, ArrivalTime = DateTimeOffset.UtcNow, EnteredConsultationAt = DateTimeOffset.UtcNow };
            db.QueueEntries.Add(legacy);
            await db.SaveChangesAsync();

            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            var session = await service.CreateQueueSessionAsync(doctor);
            await service.StartQueueSessionAsync(session.Id, doctor);
            Assert.Equal(session.Id, (await db.QueueEntries.SingleAsync(q => q.Id == legacy.Id)).DoctorQueueSessionId);
            await service.CompleteConsultationAsync(legacy.Id, doctor);

            Assert.Equal(QueueStatus.Completed, (await db.QueueEntries.SingleAsync(q => q.Id == legacy.Id)).Status);
            Assert.Contains(await db.Events.ToListAsync(), e => e.EventType == "LegacyConsultationRecovered" && e.DoctorQueueSessionId == session.Id);
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task Reschedule_checks_ownership_availability_and_working_hours()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, date) = await SeedDoctor(db);
            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            var firstStart = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc));
            var appointment = await service.BookAsync("owner", doctor, firstStart);
            var replacement = firstStart.AddHours(1);

            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RescheduleAsync(appointment.Id, "other-patient", replacement));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RescheduleAsync(appointment.Id, "owner", firstStart.AddHours(9)));
            await service.RescheduleAsync(appointment.Id, "owner", replacement);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.BookAsync("second-patient", doctor, replacement));
            Assert.Equal(replacement, (await db.Appointments.SingleAsync()).ScheduledStart);

            await service.CancelAsync(appointment.Id, "owner", "Patient requested");
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RescheduleAsync(appointment.Id, "owner", firstStart));
            Assert.Equal(AppointmentStatus.Cancelled, (await db.Appointments.SingleAsync()).Status);
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    [Fact]
    public async Task Repeating_break_blocks_matching_slots_on_later_weeks()
    {
        var (dbName, options, db) = await CreateDatabase();
        try
        {
            var (doctor, date) = await SeedDoctor(db);
            var service = new AppointmentService(db, new LocalAppointmentNotifier(db));
            var breakStart = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc));
            await service.AddBreakAsync(new DoctorBreak
            {
                DoctorId = doctor, StartsAt = breakStart, EndsAt = breakStart.AddMinutes(40),
                IsRecurring = true, RecurringDay = date.DayOfWeek
            });

            var nextWeekSlots = await service.GetAvailableSlotsAsync(doctor, date.AddDays(7));
            Assert.DoesNotContain(nextWeekSlots, slot => slot.TimeOfDay >= new TimeSpan(10, 0, 0) && slot.TimeOfDay < new TimeSpan(10, 40, 0));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.BookAsync("patient", doctor, new DateTimeOffset(date.AddDays(7).ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc))));
        }
        finally { await DeleteDatabase(dbName, options, db); }
    }

    private static async Task ActivateQueueSession(IAppointmentService service, string doctor)
    {
        var session = await service.CreateQueueSessionAsync(doctor);
        await service.StartQueueSessionAsync(session.Id, doctor);
    }

    private static async Task<(string Name, DbContextOptions<AppointmentDbContext> Options, AppointmentDbContext Context)> CreateDatabase()
    {
        var name = $"MediCareIntegration_{Guid.NewGuid():N}";
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();
        var configuredConnection = configuration.GetConnectionString("MediCareTest")
            ?? throw new InvalidOperationException("Connection string 'MediCareTest' is missing from test configuration.");
        var connection = new SqlConnectionStringBuilder(configuredConnection) { InitialCatalog = name }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppointmentDbContext>().UseSqlServer(connection).Options;
        var db = new AppointmentDbContext(options);
        await db.Database.MigrateAsync();
        return (name, options, db);
    }

    private static async Task<(string Doctor, DateOnly Date)> SeedDoctor(AppointmentDbContext db)
    {
        var doctor = $"doctor-{Guid.NewGuid():N}";
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        db.SchedulingSettings.Add(new DoctorSchedulingSettings { DoctorId = doctor, DefaultConsultationDurationMinutes = 20 });
        db.WorkingHours.Add(new DoctorWorkingHour { DoctorId = doctor, DayOfWeek = date.DayOfWeek, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(18, 0) });
        await db.SaveChangesAsync();
        return (doctor, date);
    }

    private static async Task DeleteDatabase(string name, DbContextOptions<AppointmentDbContext> options, AppointmentDbContext db)
    {
        await db.DisposeAsync();
        await using var cleanup = new AppointmentDbContext(options);
        await cleanup.Database.EnsureDeletedAsync();
    }
}
