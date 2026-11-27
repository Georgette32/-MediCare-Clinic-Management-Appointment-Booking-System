using System.Security.Cryptography;
using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Identity;
using MediCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediCare.Infrastructure.DemoData;

public sealed record DemoSeedResult(bool Seeded, string Summary);

/// <summary>
/// Development-only demo data: staff, doctors with schedules, patients, six weeks of clinic history, today's live queue,
/// the next two weeks of bookings, medical records with prescriptions and attachments, extensions, disruptions and
/// notifications. It only adds rows; existing data is never changed. Running it twice is refused.
/// </summary>
public static class DemoDataSeeder
{
    public const string DemoPassword = "Demo@Clinic2026";
    internal const string MarkerEmail = "reception.demo@medicare.test";
    /// <summary>
    /// Overrides <see cref="DemoPassword"/>. Set it whenever the database can be reached by anyone else: the default is published.
    /// </summary>
    public const string PasswordKey = "DemoData:Password";

    public static async Task<DemoSeedResult> SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(MarkerEmail) is not null)
            return new DemoSeedResult(false, $"Demo data is already present ({MarkerEmail} exists). Nothing was changed.");

        var builder = new DemoDataBuilder(
            provider.GetRequiredService<MediCareDbContext>(),
            users,
            provider.GetRequiredService<IClinicClock>(),
            provider.GetRequiredService<IMedicalFileStore>(),
            provider.GetRequiredService<IConfiguration>()[PasswordKey]);
        return await builder.BuildAsync(cancellationToken);
    }
}

internal sealed class DemoDataBuilder(
    MediCareDbContext db,
    UserManager<ApplicationUser> users,
    IClinicClock clock,
    IMedicalFileStore fileStore,
    string? passwordOverride)
{
    private readonly string password = string.IsNullOrWhiteSpace(passwordOverride) ? DemoDataSeeder.DemoPassword : passwordOverride;

    private const int HistoryDays = 42;
    private const int FutureDays = 14;

    private readonly Random rng = new(20261124);
    private DateTimeOffset now;
    private DateOnly today;

    private readonly List<DoctorCtx> doctors = [];
    private readonly List<PatientCtx> patients = [];
    private PatientCtx featured = null!;
    private Guid adminUserId;
    private Guid receptionUserId;
    private Guid reception2UserId;

    private readonly Dictionary<(Guid Doctor, DateOnly Date), HashSet<int>> takenSlots = new();
    private readonly Dictionary<(Guid Patient, DateOnly Date), List<(int Start, int End)>> patientBusy = new();
    private readonly Dictionary<(Guid Doctor, DateOnly Date), List<(int Start, int End)>> breaks = new();
    private readonly Dictionary<(Guid Doctor, DateOnly Date, int OriginalEnd), int> approvedExtensions = new();
    private readonly Dictionary<(Guid Doctor, DateOnly Date), DisruptionPlan> disruptions = new();
    private readonly Dictionary<(Guid Doctor, DateOnly Date), ForcedVisit> forced = new();
    private readonly List<(Visit Visit, QueueEntry Entry)> completed = [];
    private readonly List<(string StoredFileName, byte[] Bytes)> files = [];
    private readonly Dictionary<string, (PatientCtx[] Eligible, PatientCtx[] Followed)> patientPools = new(StringComparer.OrdinalIgnoreCase);

    private int appointmentCount;
    private int queueCount;
    private int recordCount;
    private int notificationCount;

    public async Task<DemoSeedResult> BuildAsync(CancellationToken cancellationToken)
    {
        now = clock.UtcNow;
        today = clock.Today;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        adminUserId = (await users.GetUsersInRoleAsync(MediCareRoles.Admin)).OrderBy(user => user.CreatedAtUtc).Select(user => user.Id).FirstOrDefault();
        if (adminUserId == Guid.Empty)
            throw new InvalidOperationException("No Admin account exists. Start the app once so the bootstrap Admin is created, then seed again.");

        await CreateStaffAsync();
        await CreateDoctorsAsync(cancellationToken);
        await CreatePatientsAsync();
        await db.SaveChangesAsync(cancellationToken);

        PlanSchedules();
        PlanFeaturedPatient();
        PlanRescheduleRequest();

        for (var date = today.AddDays(-HistoryDays); date <= today.AddDays(FutureDays); date = date.AddDays(1))
            foreach (var doctor in doctors)
                SimulateDay(doctor, date);

        BuildDisruptionWorklists();
        BuildDeactivatedDoctorCancellations();
        BuildMedicalRecords();
        await ConfigureClinicSettingsAsync(cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var (storedFileName, bytes) in files)
        {
            using var content = new MemoryStream(bytes);
            await fileStore.SaveAsync(content, storedFileName, cancellationToken);
        }

        var summary =
            $"Demo data added for clinic date {today:yyyy-MM-dd}: {doctors.Count} doctors, 3 receptionists, {patients.Count} patients, " +
            $"{appointmentCount} appointments, {queueCount} queue entries, {recordCount} medical records, {files.Count} attachments, " +
            $"{notificationCount} notifications. Every demo account uses " +
            (password == DemoDataSeeder.DemoPassword ? $"the password {password}." : "the password you supplied.");
        return new DemoSeedResult(true, summary);
    }

    // ---------------------------------------------------------------- accounts

    private async Task<Guid> CreateUserAsync(string userName, string? email, string fullName, string? phone, string role, bool active = true, bool mustChangePassword = false)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = email,
            EmailConfirmed = email is not null,
            FullName = fullName,
            PhoneNumber = phone,
            IsActive = active,
            MustChangePassword = mustChangePassword,
            CreatedAtUtc = now.AddDays(-HistoryDays - 5)
        };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
            throw new InvalidOperationException($"Could not create demo account {userName}: {string.Join(" ", created.Errors.Select(error => error.Description))}");
        var assigned = await users.AddToRoleAsync(user, role);
        if (!assigned.Succeeded)
            throw new InvalidOperationException($"Could not assign {role} to demo account {userName}.");
        return user.Id;
    }

    private async Task CreateStaffAsync()
    {
        receptionUserId = await CreateUserAsync(DemoDataSeeder.MarkerEmail, DemoDataSeeder.MarkerEmail, "Heba Mostafa", null, MediCareRoles.Receptionist);
        reception2UserId = await CreateUserAsync("reception2.demo@medicare.test", "reception2.demo@medicare.test", "Karim Adel", null, MediCareRoles.Receptionist);
        await CreateUserAsync("reception.former@medicare.test", "reception.former@medicare.test", "Tamer Fawzy", null, MediCareRoles.Receptionist, active: false);
    }

    private async Task CreateDoctorsAsync(CancellationToken cancellationToken)
    {
        var specializations = await db.Specializations.ToListAsync(cancellationToken);
        foreach (var spec in DemoDataCatalog.Doctors)
        {
            var specialization = specializations.FirstOrDefault(item => string.Equals(item.Name, spec.Specialization, StringComparison.OrdinalIgnoreCase));
            if (specialization is null)
            {
                specialization = new Specialization(Guid.NewGuid(), spec.Specialization);
                db.Specializations.Add(specialization);
                specializations.Add(specialization);
            }

            var userId = await CreateUserAsync(spec.Email, spec.Email, spec.FullName, null, MediCareRoles.Doctor, active: spec.Active);
            var doctor = new Doctor(Guid.NewGuid(), userId, spec.FullName, specialization.Id, spec.AppointmentDurationMinutes);
            doctor.UpdateBiography(spec.Biography);
            if (!spec.Active)
                doctor.Deactivate();
            db.Doctors.Add(doctor);

            foreach (var window in spec.Windows)
                foreach (var day in window.Days)
                    db.DoctorWorkingHours.Add(DoctorWorkingHour.Create(doctor.Id, day, T(window.StartMinute), T(window.EndMinute)));

            var context = new DoctorCtx(spec, doctor, userId);
            if (!spec.Active)
                context.LastWorkingDate = today.AddDays(-11);
            doctors.Add(context);
        }
    }

    private async Task CreatePatientsAsync()
    {
        var first = DemoDataCatalog.FirstNames;
        var last = DemoDataCatalog.LastNames;
        var profiles = DemoPatientHistories.Patients;
        for (var index = 0; index < profiles.Count; index++)
        {
            var name = $"{first[index % first.Length]} {last[(index * 7 + index / first.Length) % last.Length]}";
            var phone = $"+2010090000{index + 1:D2}";
            var email = index % 3 == 0 ? $"{name.ToLowerInvariant().Replace(' ', '.')}{index + 1}@example.com" : null;
            // The last patient was created by Reception and has not signed in yet, so the first sign-in forces a new password.
            var mustChange = index == profiles.Count - 1;
            var userId = await CreateUserAsync(phone, email, name, phone, MediCareRoles.Patient, mustChangePassword: mustChange);
            var patient = new Patient(Guid.NewGuid(), userId, name, phone);
            db.Patients.Add(patient);
            patients.Add(new PatientCtx(patient, userId, profiles[index]));
        }
        featured = patients[0];
    }

    // ---------------------------------------------------------------- schedules, extensions, disruptions

    private DoctorCtx Doc(string key) => doctors.Single(item => item.Spec.Key == key);

    private void PlanSchedules()
    {
        var youssef = Doc("youssef");
        var mariam = Doc("mariam");
        var khaled = Doc("khaled");
        var nadia = Doc("nadia");
        var amr = Doc("amr");
        var laila = Doc("laila");

        AddHoliday(khaled, today.AddDays(6), today.AddDays(8), "Orthopedics conference in Alexandria");
        AddHoliday(laila, today.AddDays(-20), today.AddDays(-19), "Annual leave");
        AddHoliday(mariam, today.AddDays(10), today.AddDays(10), "Personal leave");

        // A past disruption, fully handled: Dr Amr left his evening clinic early.
        var pastDate = PreviousDate(amr, today.AddDays(-10), date => WindowsFor(amr, date).Any(window => window.Start >= 18 * 60));
        AddDisruption(amr, pastDate, 19 * 60, "Called in for an emergency operation at the university hospital.", At(pastDate, 18 * 60 + 40), past: true);

        // An upcoming disruption with an open worklist for Reception.
        var upcomingDate = NextDate(nadia, today.AddDays(2));
        AddDisruption(nadia, upcomingDate, 18 * 60, "Travelling to a medical conference in the evening.", Min(now.AddHours(-3), now), past: false);

        // Working-hour extensions in every state.
        var mariamPast = PreviousDate(mariam, today.AddDays(-7));
        AddExtension(mariam, mariamPast, 22 * 60, 23 * 60, "Flu season: extra children waiting", WorkingHourExtensionStatus.Approved, mariam.UserId, At(mariamPast, 14 * 60), At(mariamPast, 14 * 60));
        if (WindowsFor(mariam, today).Count > 0)
            AddExtension(mariam, today, 22 * 60, 23 * 60, "Many walk-ins expected this evening", WorkingHourExtensionStatus.Approved, mariam.UserId, Min(At(today, 12 * 60), now.AddMinutes(-30)), Min(At(today, 12 * 60), now.AddMinutes(-30)));

        var youssefDate = NextDate(youssef, today.AddDays(2));
        var youssefExtension = AddExtension(youssef, youssefDate, 14 * 60, 15 * 60, "Many follow-up requests this week", WorkingHourExtensionStatus.Pending, receptionUserId, now.AddHours(-2), null);
        Notify(youssef.UserId, StaffNotificationKind.WorkingHourExtensionRequested.ToString(), "Working-hour extension requested",
            $"Reception requested extra hours on {youssefDate:yyyy-MM-dd} (until 15:00). Review it on the Extensions page.",
            "DoctorWorkingHourExtension", youssefExtension.Id, now.AddHours(-2), read: false);

        var amrRejectedDate = PreviousDate(amr, today.AddDays(-6));
        var amrRejected = AddExtension(amr, amrRejectedDate, 13 * 60, 14 * 60, "Several patients asked for a late-morning visit", WorkingHourExtensionStatus.Rejected, receptionUserId, At(amrRejectedDate.AddDays(-2), 11 * 60), At(amrRejectedDate.AddDays(-2), 13 * 60), "I have a hospital shift after 13:00 that day.");
        Notify(receptionUserId, StaffNotificationKind.WorkingHourExtensionDecided.ToString(), "Extension rejected",
            $"Your working-hour extension request for {amrRejectedDate:yyyy-MM-dd} (until 14:00) was rejected.",
            "DoctorWorkingHourExtension", amrRejected.Id, At(amrRejectedDate.AddDays(-2), 13 * 60), read: true);

        var lailaDate = NextDate(laila, today.AddDays(4));
        var lailaCancelled = AddExtension(laila, lailaDate, 17 * 60, 18 * 60, "Extra screening appointments requested", WorkingHourExtensionStatus.Cancelled, receptionUserId, now.AddDays(-1), now.AddHours(-20), cancelledAtUtc: now.AddHours(-3));
        Notify(receptionUserId, StaffNotificationKind.WorkingHourExtensionCancelled.ToString(), "Extension cancelled by the doctor",
            $"The doctor cancelled the working-hour extension you requested for {lailaDate:yyyy-MM-dd} (until 18:00).",
            "DoctorWorkingHourExtension", lailaCancelled.Id, now.AddHours(-3), read: false);

        // Dated breaks.
        AddBreak(youssef, NextDate(youssef, today.AddDays(1)), 12 * 60, 12 * 60 + 40);
        AddBreak(khaled, PreviousDate(khaled, today.AddDays(-3)), 15 * 60, 15 * 60 + 30);
        AddBreak(amr, NextDate(amr, today), 11 * 60, 11 * 60 + 20);
        AddBreak(nadia, PreviousDate(nadia, today.AddDays(-5)), 17 * 60, 17 * 60 + 40);
    }

    private void AddHoliday(DoctorCtx doctor, DateOnly start, DateOnly end, string reason)
    {
        db.DoctorHolidays.Add(DoctorHoliday.Create(doctor.Id, start, end, reason));
        doctor.Holidays.Add((start, end));
    }

    private void AddBreak(DoctorCtx doctor, DateOnly date, int start, int end)
    {
        db.DoctorBreaks.Add(DoctorBreak.Create(doctor.Id, date, T(start), T(end)));
        if (!breaks.TryGetValue((doctor.Id, date), out var list))
            breaks[(doctor.Id, date)] = list = [];
        list.Add((start, end));
    }

    private DoctorWorkingHourExtension AddExtension(
        DoctorCtx doctor, DateOnly date, int originalEnd, int extendedEnd, string reason, WorkingHourExtensionStatus status,
        Guid requestedBy, DateTimeOffset requestedAt, DateTimeOffset? decidedAt, string? notes = null, DateTimeOffset? cancelledAtUtc = null)
    {
        DoctorWorkingHourExtension extension;
        if (status == WorkingHourExtensionStatus.Approved && requestedBy == doctor.UserId)
        {
            extension = DoctorWorkingHourExtension.CreateApprovedByDoctor(doctor.Id, date, T(originalEnd), T(extendedEnd), reason, requestedBy, requestedAt);
        }
        else
        {
            extension = DoctorWorkingHourExtension.Request(doctor.Id, date, T(originalEnd), T(extendedEnd), reason, requestedBy, requestedAt);
            if (status is WorkingHourExtensionStatus.Approved or WorkingHourExtensionStatus.Cancelled)
                extension.Approve(decidedAt!.Value, notes);
            else if (status == WorkingHourExtensionStatus.Rejected)
                extension.Reject(decidedAt!.Value, notes);
            if (status == WorkingHourExtensionStatus.Cancelled)
                extension.Cancel(cancelledAtUtc!.Value);
        }

        if (status == WorkingHourExtensionStatus.Approved)
            approvedExtensions[(doctor.Id, date, originalEnd)] = extendedEnd;
        db.DoctorWorkingHourExtensions.Add(extension);
        return extension;
    }

    private void AddDisruption(DoctorCtx doctor, DateOnly date, int fromMinute, string reason, DateTimeOffset recordedAt, bool past)
    {
        var disruption = DoctorDisruption.Record(doctor.Id, date, T(fromMinute), reason, receptionUserId, recordedAt);
        db.DoctorDisruptions.Add(disruption);
        var plan = new DisruptionPlan(doctor, date, fromMinute, disruption, past);
        disruptions[(doctor.Id, date)] = plan;

        foreach (var window in WindowsFor(doctor, date))
        {
            if (window.End <= fromMinute)
                continue;
            var blockStart = Math.Max(window.Start, fromMinute);
            db.DoctorBreaks.Add(DoctorBreak.CreateForDisruption(doctor.Id, date, T(blockStart), T(window.End), disruption.Id));
        }

        if (past)
            disruption.RecordDayEnd(receptionUserId, At(date, 22 * 60 + 15));
    }

    // ---------------------------------------------------------------- featured scenarios

    /// <summary>Salma Adel (01009000001) gets a full story: past visits with records, files, a no-show, a rejection and upcoming bookings.</summary>
    private void PlanFeaturedPatient()
    {
        var youssef = Doc("youssef");
        var khaled = Doc("khaled");
        var nadia = Doc("nadia");
        var amr = Doc("amr");
        var laila = Doc("laila");
        var cases = DemoDataCatalog.Cases;

        forced[(amr.Id, PreviousDate(amr, today.AddDays(-33)))] = new ForcedVisit(featured, Outcome.Completed, cases["Internal Medicine"][0], FeaturedFile.LabReport,
            "Lab results reviewed: HbA1c 7.8%, LDL 141 mg/dL, kidney function normal. Diet plan reinforced.");
        forced[(laila.Id, PreviousDate(laila, today.AddDays(-24)))] = new ForcedVisit(featured, Outcome.NoShow, null, FeaturedFile.None, null);
        forced[(nadia.Id, PreviousDate(nadia, today.AddDays(-16)))] = new ForcedVisit(featured, Outcome.Completed, cases["Dermatology"][2], FeaturedFile.SkinPhoto, null);
        forced[(khaled.Id, PreviousDate(khaled, today.AddDays(-14)))] = new ForcedVisit(featured, Outcome.Rejected, null, FeaturedFile.None, null);
        forced[(youssef.Id, PreviousDate(youssef, today.AddDays(-9)))] = new ForcedVisit(featured, Outcome.Completed, cases["Cardiology"][1], FeaturedFile.Ecg, null);
        forced[(amr.Id, PreviousDate(amr, today.AddDays(-3)))] = new ForcedVisit(featured, Outcome.Completed, cases["Internal Medicine"][4], FeaturedFile.None, null);

        // Upcoming: a confirmed visit, a pending request, a visit moved by the clinic, and one she cancelled herself.
        var (confirmedDate, confirmedStart) = ReserveSlot(youssef, today.AddDays(3), featured);
        var confirmed = NewAppointment(featured, youssef, confirmedDate, confirmedStart, now.AddDays(-2));
        confirmed.Confirm(now.AddDays(-2).AddHours(3));
        NotifyPatient(featured, PatientNotificationKind.BookingRequestConfirmed, "Booking confirmed",
            $"Your appointment on {When(confirmedDate, confirmedStart)} is confirmed.", confirmed.Id, now.AddDays(-2).AddHours(3), forceUnread: true);

        var (pendingDate, pendingStart) = ReserveSlot(laila, today.AddDays(6), featured);
        var pending = NewAppointment(featured, laila, pendingDate, pendingStart, now.AddHours(-5));
        NotifyReception(StaffNotificationKind.AppointmentRequested, "New appointment request",
            $"A patient requested an appointment with {laila.Spec.FullName} on {pendingDate:yyyy-MM-dd} at {T(pendingStart):h\\:mm tt}. Confirm or reject it on the Requests page.",
            pending.Id, now.AddHours(-5));

        var (originalDate, originalStart) = ReserveSlot(amr, today.AddDays(2), featured);
        var original = NewAppointment(featured, amr, originalDate, originalStart, now.AddDays(-6));
        original.Confirm(now.AddDays(-6));
        var (movedDate, movedStart) = ReserveSlot(amr, originalDate.AddDays(3), featured);
        var replacement = original.ReplaceByClinic(amr.Id, movedDate, T(movedStart), T(movedStart + amr.Duration), now.AddDays(-1));
        db.Appointments.Add(replacement);
        appointmentCount++;
        NotifyPatient(featured, PatientNotificationKind.AppointmentRescheduledByClinic, "Appointment moved",
            $"Your appointment has been moved to {When(movedDate, movedStart)}.", replacement.Id, now.AddDays(-1), forceUnread: true);

        var (cancelDate, cancelStart) = ReserveSlot(khaled, today.AddDays(4), featured);
        var cancelled = NewAppointment(featured, khaled, cancelDate, cancelStart, now.AddDays(-8));
        cancelled.Confirm(now.AddDays(-8).AddHours(2));
        cancelled.CancelByPatient(today, now.AddHours(-20), "Travelling that week");
    }

    /// <summary>A patient asked to move a confirmed visit; Reception still has to confirm or reject the request.</summary>
    private void PlanRescheduleRequest()
    {
        var laila = Doc("laila");
        var patient = patients[1];
        var (originalDate, originalStart) = ReserveSlot(laila, today.AddDays(2), patient);
        var original = NewAppointment(patient, laila, originalDate, originalStart, now.AddDays(-7));
        original.Confirm(now.AddDays(-7).AddHours(1));
        var (newDate, newStart) = ReserveSlot(laila, originalDate.AddDays(1), patient);
        var request = original.CreateRescheduledReplacement(laila.Id, newDate, T(newStart), T(newStart + laila.Duration), now.AddHours(-4));
        db.Appointments.Add(request);
        appointmentCount++;
        NotifyReception(StaffNotificationKind.RescheduleRequested, "New reschedule request",
            $"A patient asked to move their {originalDate:yyyy-MM-dd} {T(originalStart):h\\:mm tt} appointment to {newDate:yyyy-MM-dd} at {T(newStart):h\\:mm tt} with {laila.Spec.FullName}. Confirm or reject it on the Requests page.",
            request.Id, now.AddHours(-4));
    }

    // ---------------------------------------------------------------- day simulation

    private void SimulateDay(DoctorCtx doctor, DateOnly date)
    {
        var windows = WindowsFor(doctor, date);
        if (windows.Count == 0)
            return;

        disruptions.TryGetValue((doctor.Id, date), out var disruption);
        forced.Remove((doctor.Id, date), out var forcedVisit);
        var isFuture = date > today;
        var visits = new List<Visit>();

        foreach (var (windowStart, start) in FreeSlotsWithWindow(doctor, date))
        {
            var end = start + doctor.Duration;
            var disrupted = disruption is not null && end > disruption.FromMinute;
            PatientCtx? patient;
            Outcome outcome;
            ForcedVisit? usedForced = null;
            if (forcedVisit is not null && !disrupted && IsPatientFree(forcedVisit.Patient, date, start, end))
            {
                usedForced = forcedVisit;
                forcedVisit = null;
                patient = usedForced.Patient;
                outcome = usedForced.Outcome;
            }
            else
            {
                var fill = disrupted ? 0.9 : isFuture ? Math.Max(0.15, 0.65 - 0.035 * (date.DayNumber - today.DayNumber - 1)) : 0.72;
                if (!Chance(fill))
                    continue;
                patient = PickPatient(doctor, date, start, end);
                if (patient is null)
                    continue;
                outcome = disrupted ? Outcome.Disrupted : RollOutcome(date);
            }

            MarkBusy(patient, date, start, end);
            Take(doctor.Id, date, start);
            var visit = new Visit(doctor, date, patient, outcome)
            {
                StartMinute = start,
                EndMinute = end,
                WindowStartMinute = windowStart,
                Forced = usedForced
            };
            visits.Add(visit);
        }

        if (!isFuture)
        {
            AddWalkIns(doctor, date, windows, disruption, visits);
            Serve(doctor, date, windows, visits);
        }

        foreach (var visit in visits)
        {
            if (visit.WalkIn)
                MaterializeWalkIn(visit);
            else
                MaterializeScheduled(visit, disruption);
        }
    }

    private Outcome RollOutcome(DateOnly date)
    {
        var roll = rng.NextDouble();
        if (date > today)
            return roll < 0.05 ? Outcome.CancelledByPatient : Outcome.Upcoming;
        if (roll < 0.76) return Outcome.Completed;
        if (roll < 0.84) return Outcome.NoShow;
        if (roll < 0.90) return Outcome.CancelledByPatient;
        if (roll < 0.93) return Outcome.Rejected;
        if (roll < 0.95) return Outcome.CancelledByClinic;
        return date == today ? Outcome.Completed : Outcome.LeftBeforeConsultation;
    }

    private void AddWalkIns(DoctorCtx doctor, DateOnly date, List<(int Start, int End)> windows, DisruptionPlan? disruption, List<Visit> visits)
    {
        var count = date == today ? rng.Next(2, 5) : rng.Next(0, 4);
        var walkIns = new List<Visit>();
        for (var index = 0; index < count; index++)
        {
            var window = windows[rng.Next(windows.Count)];
            var latest = window.End - 30;
            if (disruption is not null)
                latest = Math.Min(latest, disruption.FromMinute - 40);
            var earliest = window.Start - 5;
            if (latest <= earliest)
                continue;

            var arrival = At(date, rng.Next(earliest, latest)).AddSeconds(rng.Next(0, 60));
            if (arrival > now)
                continue;
            var patient = PickWalkInPatient(doctor, date);
            if (patient is null)
                continue;
            MarkBusy(patient, date, 0, 24 * 60 - 1);

            var requested = arrival.AddSeconds(rng.Next(60, 240));
            if (requested > now)
                requested = arrival;
            var decided = requested.AddSeconds(rng.Next(60, 480));
            var rejected = Chance(0.12);
            walkIns.Add(new Visit(doctor, date, patient, rejected ? Outcome.WalkInRejected : Outcome.Completed)
            {
                WalkIn = true,
                WindowStartMinute = window.Start,
                Arrival = arrival,
                ApprovalRequested = requested,
                ApprovalDecided = decided,
                Ready = Max(arrival, decided)
            });
        }

        var sequence = 1;
        foreach (var walkIn in walkIns.OrderBy(item => item.Arrival))
            walkIn.Sequence = sequence++;
        visits.AddRange(walkIns);
    }

    /// <summary>
    /// Plays the doctor's day: checked-in scheduled patients are called by appointment time, then approved walk-ins in
    /// arrival order. Consultation lengths vary around the configured duration, so analytics have realistic spread.
    /// </summary>
    private void Serve(DoctorCtx doctor, DateOnly date, List<(int Start, int End)> windows, List<Visit> visits)
    {
        foreach (var visit in visits.Where(item => !item.WalkIn && item.Outcome is Outcome.Completed or Outcome.LeftBeforeConsultation))
        {
            var slotStart = At(date, visit.StartMinute);
            visit.Arrival = Chance(0.15)
                ? slotStart.AddSeconds(rng.Next(60, 12 * 60))
                : slotStart.AddSeconds(-rng.Next(0, 25 * 60));
            visit.Ready = Max(visit.Arrival, At(date, visit.WindowStartMinute));
        }

        var remaining = visits.Where(item => item.Outcome == Outcome.Completed).ToList();
        var free = At(date, windows[0].Start);
        while (remaining.Count > 0)
        {
            var ready = remaining.Where(item => item.Ready <= free).ToList();
            if (ready.Count == 0)
            {
                free = remaining.Min(item => item.Ready);
                continue;
            }

            var next = ready.Where(item => !item.WalkIn).OrderBy(item => item.StartMinute).FirstOrDefault()
                ?? ready.OrderBy(item => item.Arrival).First();
            remaining.Remove(next);

            var factor = 0.55 + rng.NextDouble() * 0.85 + (Chance(0.1) ? 0.5 : 0);
            next.Called = free.AddSeconds(rng.Next(15, 150));
            next.Started = next.Called.Value.AddSeconds(rng.Next(40, 200));
            next.Completed = next.Started.Value.AddSeconds(Math.Max(240, (int)(doctor.Duration * 60 * factor)));
            free = next.Completed.Value;
        }
    }

    private void MaterializeScheduled(Visit visit, DisruptionPlan? disruption)
    {
        var doctor = visit.Doctor;
        var date = visit.Date;
        var slotStart = At(date, visit.StartMinute);
        var byClinic = visit.Outcome != Outcome.Rejected && visit.Forced is null && Chance(0.3);

        var created = date > today
            ? now.AddMinutes(-rng.Next(30, 60 * 24 * 9))
            : slotStart.AddMinutes(-rng.Next(60 * 24, 60 * 24 * 12));
        if (visit.Outcome == Outcome.CancelledByPatient)
            created = Min(created, At(date, 0).AddDays(-2));
        if (visit.Outcome == Outcome.Disrupted)
            created = Min(created, disruption!.Entity.RecordedAtUtc.AddHours(-rng.Next(2, 72)));
        if (created > now)
            created = now.AddMinutes(-rng.Next(5, 120));

        var appointment = NewAppointment(visit.Patient, doctor, date, visit.StartMinute, created);
        visit.Appointment = appointment;
        var confirmAt = byClinic ? created : Min(Min(created.AddMinutes(rng.Next(10, 600)), now), Max(created, slotStart.AddMinutes(-30)));

        switch (visit.Outcome)
        {
            case Outcome.Upcoming:
                if (!byClinic && Chance(0.3))
                {
                    if (created > now.AddDays(-3))
                        NotifyReception(StaffNotificationKind.AppointmentRequested, "New appointment request",
                            $"A patient requested an appointment with {doctor.Spec.FullName} on {date:yyyy-MM-dd} at {T(visit.StartMinute):h\\:mm tt}. Confirm or reject it on the Requests page.",
                            appointment.Id, created);
                }
                else
                {
                    appointment.Confirm(confirmAt);
                    NotifyConfirmed(visit, byClinic, confirmAt);
                }
                break;

            case Outcome.Completed:
                appointment.Confirm(confirmAt);
                NotifyConfirmed(visit, byClinic, confirmAt);
                if (visit.Arrival <= now)
                {
                    var entry = QueueEntry.CreateScheduled(visit.Patient.Entity.Id, doctor.Id, appointment.Id, date, visit.Arrival);
                    db.QueueEntries.Add(entry);
                    queueCount++;
                    Progress(entry, visit, approval: null);
                    if (entry.Status == QueueEntryStatus.Completed)
                    {
                        appointment.MarkCompleted(visit.Completed!.Value);
                        completed.Add((visit, entry));
                    }
                }
                break;

            case Outcome.NoShow:
                appointment.Confirm(confirmAt);
                NotifyConfirmed(visit, byClinic, confirmAt);
                if (slotStart <= now)
                {
                    var grace = doctor.Duration * 3;
                    appointment.StartGracePeriod(grace, slotStart);
                    var expiry = slotStart.AddMinutes(grace);
                    if (expiry <= now)
                        appointment.MarkNoShow(Min(expiry.AddSeconds(rng.Next(0, 300)), now));
                }
                break;

            case Outcome.CancelledByPatient:
            {
                var latest = Min(now, At(date, 0).AddHours(-2));
                var cancelAt = created.AddSeconds(rng.NextDouble() * Math.Max(60, (latest - created).TotalSeconds));
                if (cancelAt > latest)
                    cancelAt = latest;
                if (Chance(0.6))
                    appointment.Confirm(Min(confirmAt, cancelAt));
                appointment.CancelByPatient(date.AddDays(-1), cancelAt, Chance(0.5) ? "Personal reasons" : null);
                break;
            }

            case Outcome.Rejected:
            {
                var rejectAt = Min(created.AddMinutes(rng.Next(60, 480)), now);
                appointment.Reject(rejectAt);
                NotifyPatient(visit.Patient, PatientNotificationKind.BookingRequestRejected, "Booking not approved",
                    $"Your booking request for {When(date, visit.StartMinute)} could not be approved. Please choose another time.",
                    appointment.Id, rejectAt);
                break;
            }

            case Outcome.CancelledByClinic:
            {
                appointment.Confirm(confirmAt);
                var latest = Min(now, slotStart.AddHours(-1));
                var cancelAt = Max(confirmAt, Min(confirmAt.AddHours(rng.Next(2, 48)), latest));
                const string reason = "The doctor's schedule changed for that day";
                appointment.CancelByClinic(reason, cancelAt);
                NotifyPatient(visit.Patient, PatientNotificationKind.AppointmentCancelledByClinic, "Appointment cancelled",
                    $"Your appointment on {When(date, visit.StartMinute)} was cancelled by the clinic. Reason: {reason}. Please contact the clinic to rebook.",
                    appointment.Id, cancelAt);
                break;
            }

            case Outcome.LeftBeforeConsultation:
            {
                appointment.Confirm(confirmAt);
                var entry = QueueEntry.CreateScheduled(visit.Patient.Entity.Id, doctor.Id, appointment.Id, date, visit.Arrival);
                entry.Cancel(Max(visit.Arrival, Min(visit.Arrival.AddMinutes(10), now)));
                db.QueueEntries.Add(entry);
                queueCount++;
                appointment.CancelByClinic(AppointmentCancellationReasons.PatientLeftBeforeConsultation, Min(At(date.AddDays(1), 5), now));
                break;
            }

            case Outcome.Disrupted:
                appointment.Confirm(Min(confirmAt, disruption!.Entity.RecordedAtUtc.AddMinutes(-30)));
                disruption.Affected.Add(visit);
                break;
        }
    }

    private void MaterializeWalkIn(Visit visit)
    {
        var doctor = visit.Doctor;
        var entry = QueueEntry.CreateWalkIn(visit.Patient.Entity.Id, doctor.Id, visit.Date, visit.Arrival, visit.Sequence);
        db.QueueEntries.Add(entry);
        queueCount++;

        var requested = visit.ApprovalRequested!.Value;
        var decided = visit.ApprovalDecided!.Value;
        var approval = WalkInAdmissionApproval.Request(entry, requested, requested.AddMinutes(30));
        db.WalkInAdmissionApprovals.Add(approval);
        var isToday = visit.Date == today;

        if (isToday || requested > now.AddDays(-3))
            Notify(doctor.UserId, StaffNotificationKind.WalkInAdmissionRequested.ToString(), "Walk-in admission requested",
                $"Reception requested admission approval for walk-in queue position {visit.Sequence}. Review it on the Walk-in admissions page.",
                "WalkInAdmissionApproval", approval.Id, requested, read: decided <= now);

        if (decided > now)
            return; // Still waiting for the doctor's decision.

        if (visit.Outcome == Outcome.WalkInRejected)
        {
            approval.Reject(decided, Pick(DemoDataCatalog.WalkInRejectionNotes));
            if (decided.AddMinutes(4) <= now)
                entry.Cancel(Max(visit.Arrival, decided.AddMinutes(4)));
            if (isToday)
                NotifyReception(StaffNotificationKind.WalkInAdmissionDecided, "Walk-in admission rejected",
                    $"{doctor.Spec.FullName} rejected the walk-in at queue position {visit.Sequence}.",
                    approval.Id, decided, type: "WalkInAdmissionApproval");
            return;
        }

        approval.Approve(decided);
        if (isToday)
            NotifyReception(StaffNotificationKind.WalkInAdmissionDecided, "Walk-in admission approved",
                $"{doctor.Spec.FullName} approved the walk-in at queue position {visit.Sequence}. You can call the patient from the Queue page.",
                approval.Id, decided, type: "WalkInAdmissionApproval");
        Progress(entry, visit, approval);
        if (entry.Status == QueueEntryStatus.Completed)
            completed.Add((visit, entry));
    }

    /// <summary>Moves the attendance forward as far as the current time allows (today's queue stops at "now").</summary>
    private void Progress(QueueEntry entry, Visit visit, WalkInAdmissionApproval? approval)
    {
        if (visit.Called is null || visit.Called > now)
            return;
        if (approval is null)
            entry.Call(visit.Called.Value);
        else
            entry.CallWalkIn(approval, visit.Called.Value, visit.Date);

        if (visit.Started > now)
            return;
        entry.StartConsultation(visit.Started!.Value);

        if (visit.Completed > now)
            return;
        entry.Complete(visit.Completed!.Value);
    }

    // ---------------------------------------------------------------- disruptions and deactivation

    private void BuildDisruptionWorklists()
    {
        foreach (var plan in disruptions.Values)
        {
            var index = 0;
            foreach (var visit in plan.Affected.OrderBy(item => item.StartMinute))
            {
                var appointment = visit.Appointment!;
                var recordedAt = plan.Entity.RecordedAtUtc;
                var item = DisruptionWorklistItem.CreateForAppointment(plan.Entity.Id, visit.Patient.Entity.Id, appointment.Id, null, recordedAt);
                db.DisruptionWorklistItems.Add(item);

                if (plan.Past)
                    ResolvePastItem(plan, visit, item, index);
                else
                    ResolveUpcomingItem(plan, visit, item, index);
                index++;
            }
        }
    }

    private void ResolvePastItem(DisruptionPlan plan, Visit visit, DisruptionWorklistItem item, int index)
    {
        var appointment = visit.Appointment!;
        var actionAt = plan.Entity.RecordedAtUtc.AddMinutes(10 + index * 6);
        switch (index % 3)
        {
            case 0:
            {
                db.DisruptionContactAttempts.Add(item.LogContact(receptionUserId, "Patient agreed to move the visit.", actionAt));
                var (newDate, newStart) = ReserveSlot(plan.Doctor, today.AddDays(1), visit.Patient);
                var replacement = appointment.ReplaceByClinic(plan.Doctor.Id, newDate, T(newStart), T(newStart + plan.Doctor.Duration), actionAt.AddMinutes(3));
                db.Appointments.Add(replacement);
                appointmentCount++;
                db.DisruptionContactAttempts.Add(item.Resolve(DisruptionOutcome.Rescheduled, receptionUserId, $"Moved to {newDate:yyyy-MM-dd}.", actionAt.AddMinutes(3)));
                NotifyPatient(visit.Patient, PatientNotificationKind.AppointmentRescheduledByClinic, "Appointment moved",
                    $"Your appointment has been moved to {When(newDate, newStart)}.", replacement.Id, actionAt.AddMinutes(3));
                break;
            }
            case 1:
                db.DisruptionContactAttempts.Add(item.LogContact(receptionUserId, "No answer. Left a voice message.", actionAt));
                appointment.CancelByClinic(AppointmentCancellationReasons.ClinicUnavailable, actionAt.AddMinutes(20));
                db.DisruptionContactAttempts.Add(item.Resolve(DisruptionOutcome.Cancelled, receptionUserId, "Patient will call to rebook.", actionAt.AddMinutes(20)));
                NotifyClinicUnavailable(visit, actionAt.AddMinutes(20));
                break;
            default:
            {
                var processedAt = At(plan.Date, 22 * 60 + 30);
                appointment.CancelByClinic(AppointmentCancellationReasons.ClinicUnavailable, processedAt);
                item.ResolveAutomatically(DisruptionOutcome.Cancelled, processedAt);
                NotifyClinicUnavailable(visit, processedAt);
                break;
            }
        }
    }

    private void ResolveUpcomingItem(DisruptionPlan plan, Visit visit, DisruptionWorklistItem item, int index)
    {
        var appointment = visit.Appointment!;
        switch (index)
        {
            case 2:
                db.DisruptionContactAttempts.Add(item.LogContact(receptionUserId, "Patient asked to be called back tomorrow morning.", now.AddHours(-2)));
                break;
            case 3:
            {
                var actionAt = now.AddMinutes(-90);
                db.DisruptionContactAttempts.Add(item.LogContact(reception2UserId, "Patient prefers an earlier day.", actionAt.AddMinutes(-5)));
                var (newDate, newStart) = ReserveSlot(plan.Doctor, plan.Date.AddDays(1), visit.Patient);
                var replacement = appointment.ReplaceByClinic(plan.Doctor.Id, newDate, T(newStart), T(newStart + plan.Doctor.Duration), actionAt);
                db.Appointments.Add(replacement);
                appointmentCount++;
                db.DisruptionContactAttempts.Add(item.Resolve(DisruptionOutcome.Rescheduled, reception2UserId, $"Moved to {newDate:yyyy-MM-dd}.", actionAt));
                NotifyPatient(visit.Patient, PatientNotificationKind.AppointmentRescheduledByClinic, "Appointment moved",
                    $"Your appointment has been moved to {When(newDate, newStart)}.", replacement.Id, actionAt);
                break;
            }
            case 4:
            {
                var actionAt = now.AddMinutes(-60);
                db.DisruptionContactAttempts.Add(item.LogContact(receptionUserId, "Patient cannot come on another day this week.", actionAt.AddMinutes(-3)));
                appointment.CancelByClinic(AppointmentCancellationReasons.ClinicUnavailable, actionAt);
                db.DisruptionContactAttempts.Add(item.Resolve(DisruptionOutcome.Cancelled, receptionUserId, "Patient will book again next month.", actionAt));
                NotifyClinicUnavailable(visit, actionAt);
                break;
            }
            // Other items stay open (Pending) for Reception to work on.
        }
    }

    private void NotifyClinicUnavailable(Visit visit, DateTimeOffset at) =>
        NotifyPatient(visit.Patient, PatientNotificationKind.AppointmentCancelledClinicUnavailable, "Appointment cancelled",
            $"Your appointment on {When(visit.Date, visit.StartMinute)} could not take place because the clinic was unavailable. Please contact the clinic to rebook.",
            visit.Appointment!.Id, at);

    /// <summary>Dr Hany was deactivated ten days ago; his remaining bookings were cancelled and the patients told.</summary>
    private void BuildDeactivatedDoctorCancellations()
    {
        var hany = Doc("hany");
        var deactivatedAt = At(today.AddDays(-10), 10 * 60);
        var made = 0;
        for (var date = today.AddDays(-9); date <= today.AddDays(10) && made < 4; date = date.AddDays(1))
        {
            var window = hany.Spec.Windows.FirstOrDefault(item => item.Days.Contains(date.DayOfWeek));
            if (window is null)
                continue;
            var start = window.StartMinute + hany.Duration * rng.Next(0, 4);
            var patient = PickPatient(hany, date, start, start + hany.Duration);
            if (patient is null)
                continue;
            MarkBusy(patient, date, start, start + hany.Duration);
            var appointment = NewAppointment(patient, hany, date, start, deactivatedAt.AddDays(-rng.Next(2, 9)));
            appointment.Confirm(appointment.CreatedAtUtc.AddHours(2));
            appointment.CancelByClinic(AppointmentCancellationReasons.DoctorNoLongerAvailable, deactivatedAt);
            NotifyPatient(patient, PatientNotificationKind.AppointmentCancelledDoctorNoLongerAvailable, "Appointment cancelled",
                $"Your appointment on {When(date, start)} cannot take place because the doctor is no longer available. Please contact the clinic to rebook.",
                appointment.Id, deactivatedAt);
            made++;
        }
    }

    // ---------------------------------------------------------------- medical records

    private void BuildMedicalRecords()
    {
        var attachmentsLeft = 12;
        foreach (var (visit, entry) in completed.OrderBy(item => item.Entry.CompletedAt))
        {
            var doctor = visit.Doctor;
            var forcedVisit = visit.Forced;
            if (forcedVisit is null && !Chance(0.88))
                continue;

            var patient = visit.Patient;
            var (clinicalCase, followUp) = forcedVisit?.Case is { } forcedCase ? (forcedCase, true) : NextCase(patient, doctor);
            var notes = clinicalCase.ClinicalNotes;
            if (!patient.HistoryRecorded)
            {
                notes = $"Medical history (taken at the first visit): {patient.Profile.Background}\n\n{notes}";
                patient.HistoryRecorded = true;
            }
            var createdAt = Min(entry.CompletedAt!.Value.AddSeconds(rng.Next(30, 360)), now);
            var record = MedicalRecord.CreateForVisit(entry, doctor.Id, clinicalCase.ChiefComplaint, clinicalCase.Diagnosis, notes, createdAt);

            // Follow-up visits of a long-running condition nearly always renew the prescription.
            if (clinicalCase.Medications.Length > 0 && (forcedVisit is not null || Chance(followUp ? 0.95 : 0.85)))
                record.SetPrescription(doctor.Id, record.VisitDate, clinicalCase.Medications, clinicalCase.PrescriptionNotes, Min(createdAt.AddSeconds(rng.Next(30, 180)), now));

            var addendumText = forcedVisit?.Addendum ?? (Chance(0.05) ? Pick(DemoDataCatalog.Addenda) : null);
            if (addendumText is not null && record.VisitDate < today)
            {
                var addendumDate = record.VisitDate.AddDays(Math.Min(rng.Next(1, 6), today.DayNumber - record.VisitDate.DayNumber));
                record.AddAddendum(doctor.Id, addendumDate, addendumText, Min(At(addendumDate, 10 * 60 + rng.Next(0, 300)), now));
            }

            var file = forcedVisit?.File ?? FeaturedFile.None;
            if (file == FeaturedFile.None && attachmentsLeft > 0 && record.VisitDate < today && Chance(0.03))
                file = doctor.Spec.Specialization == "Dermatology" ? FeaturedFile.SkinPhoto : FeaturedFile.LabReport;
            if (file != FeaturedFile.None)
            {
                AddAttachment(record, visit, file, createdAt);
                attachmentsLeft--;
            }

            db.MedicalRecords.Add(record);
            recordCount++;
        }
    }

    /// <summary>
    /// The next visit of the patient's condition for this specialization, in order. Once the planned visits are used up,
    /// half of the later visits repeat the maintenance review and half are unrelated minor problems. A patient whose file
    /// has nothing for this specialization gets a minor problem.
    /// </summary>
    private (DemoClinicalCase Case, bool FollowUp) NextCase(PatientCtx patient, DoctorCtx doctor)
    {
        var specialization = doctor.Spec.Specialization;
        var condition = patient.Profile.ConditionFor(specialization);
        if (condition is not null)
        {
            var seen = patient.RecordsBySpecialization.GetValueOrDefault(specialization);
            patient.RecordsBySpecialization[specialization] = seen + 1;
            if (seen < condition.Visits.Length)
                return (condition.Visits[seen], true);
            if (Chance(0.5))
                return (condition.Visits[^1], true);
        }

        var acute = DemoPatientHistories.Acute.TryGetValue(specialization, out var pool) && pool.Length > 0
            ? pool
            : DemoDataCatalog.Cases[specialization];
        return (Pick(acute), false);
    }

    private void AddAttachment(MedicalRecord record, Visit visit, FeaturedFile file, DateTimeOffset recordCreatedAt)
    {
        var patientName = visit.Patient.Entity.FullName;
        var visitDate = record.VisitDate.ToString("yyyy-MM-dd");
        var (bytes, contentType, fileName, description) = file switch
        {
            FeaturedFile.SkinPhoto => (DemoDocuments.SkinPhoto(), MedicalAttachment.ContentTypePng, "rash-photo.png", "Photo of the affected skin area"),
            FeaturedFile.Ecg => (DemoDocuments.Pdf("12-lead ECG report", [
                $"Patient: {patientName}", $"Date: {visitDate}", "",
                "Rhythm: sinus rhythm, rate 88 bpm", "PR 150 ms, QRS 92 ms, QTc 412 ms",
                "No ST-segment or T-wave changes.", "Conclusion: normal ECG at rest."
            ]), MedicalAttachment.ContentTypePdf, "ecg-report.pdf", "Resting ECG"),
            _ => (DemoDocuments.Pdf("Laboratory results", [
                $"Patient: {patientName}", $"Sample date: {visitDate}", "",
                "HbA1c ............................ 7.8 %     (4.0 - 5.6)",
                "Fasting glucose .................. 142 mg/dL (70 - 100)",
                "Total cholesterol ................ 212 mg/dL (< 200)",
                "LDL cholesterol .................. 141 mg/dL (< 100)",
                "Creatinine ....................... 0.8 mg/dL (0.6 - 1.1)",
                "Haemoglobin ...................... 12.9 g/dL (12 - 15.5)"
            ]), MedicalAttachment.ContentTypePdf, "lab-results.pdf", "Blood tests ordered at the visit")
        };

        var storedFileName = MedicalAttachment.CreateStoredFileName(contentType);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var uploadedAt = Min(recordCreatedAt.AddHours(file == FeaturedFile.LabReport ? 26 : 0).AddMinutes(rng.Next(2, 20)), now);
        var attachment = MedicalAttachment.Create(record.Id, visit.Doctor.Id, fileName, contentType, bytes.Length, storedFileName, sha256, description, uploadedAt);
        record.AddAttachment(attachment);
        files.Add((storedFileName, bytes));
    }

    private async Task ConfigureClinicSettingsAsync(CancellationToken cancellationToken)
    {
        if (await db.ClinicSettings.AnyAsync(cancellationToken))
            return;
        var settings = ClinicSettings.CreateEmpty(now);
        settings.SetMinimumConsultationSampleSize(10, adminUserId, now);
        db.ClinicSettings.Add(settings);
    }

    // ---------------------------------------------------------------- notifications

    private void NotifyConfirmed(Visit visit, bool byClinic, DateTimeOffset at)
    {
        if (at < now.AddDays(-21))
            return;
        if (byClinic)
            NotifyPatient(visit.Patient, PatientNotificationKind.AppointmentBookedByClinic, "Appointment booked",
                $"The clinic booked an appointment for you with {visit.Doctor.Spec.FullName} on {visit.Date:yyyy-MM-dd} at {T(visit.StartMinute):h\\:mm tt}. It is confirmed.",
                visit.Appointment!.Id, at);
        else
            NotifyPatient(visit.Patient, PatientNotificationKind.BookingRequestConfirmed, "Booking confirmed",
                $"Your appointment on {When(visit.Date, visit.StartMinute)} is confirmed.", visit.Appointment!.Id, at);
    }

    private void NotifyPatient(PatientCtx patient, PatientNotificationKind kind, string title, string message, Guid appointmentId, DateTimeOffset at, bool forceUnread = false)
    {
        if (at < now.AddDays(-21) && patient != featured)
            return;
        var read = !forceUnread && at < now.AddDays(-2);
        Notify(patient.UserId, kind.ToString(), title, message, "Appointment", appointmentId, at, read);
    }

    private void NotifyReception(StaffNotificationKind kind, string title, string message, Guid relatedId, DateTimeOffset at, string type = "Appointment")
    {
        foreach (var userId in new[] { receptionUserId, reception2UserId })
            Notify(userId, kind.ToString(), title, message, type, relatedId, at, read: at < now.AddDays(-1));
    }

    private void Notify(Guid userId, string kind, string title, string message, string? type, Guid? relatedId, DateTimeOffset at, bool read)
    {
        var createdAt = Min(at, now);
        var notification = Notification.Create(userId, kind, title, message, type, relatedId, null, createdAt);
        if (read)
            notification.MarkRead(Min(createdAt.AddMinutes(rng.Next(5, 600)), now));
        db.Notifications.Add(notification);
        notificationCount++;
    }

    // ---------------------------------------------------------------- slot and patient bookkeeping

    private Appointment NewAppointment(PatientCtx patient, DoctorCtx doctor, DateOnly date, int start, DateTimeOffset createdAt)
    {
        var appointment = Appointment.Request(patient.Entity.Id, doctor.Id, date, T(start), T(start + doctor.Duration), Min(createdAt, now));
        db.Appointments.Add(appointment);
        appointmentCount++;
        return appointment;
    }

    private List<(int Start, int End)> WindowsFor(DoctorCtx doctor, DateOnly date)
    {
        if (doctor.Holidays.Any(holiday => date >= holiday.Start && date <= holiday.End))
            return [];
        if (doctor.LastWorkingDate is { } last && date > last)
            return [];
        return doctor.Spec.Windows
            .Where(window => window.Days.Contains(date.DayOfWeek))
            .Select(window => (window.StartMinute, approvedExtensions.TryGetValue((doctor.Id, date, window.EndMinute), out var extended) ? extended : window.EndMinute))
            .OrderBy(window => window.StartMinute)
            .ToList();
    }

    private IEnumerable<(int WindowStart, int Start)> FreeSlotsWithWindow(DoctorCtx doctor, DateOnly date)
    {
        foreach (var (windowStart, windowEnd) in WindowsFor(doctor, date))
        {
            for (var start = windowStart; start + doctor.Duration <= windowEnd; start += doctor.Duration)
            {
                var end = start + doctor.Duration;
                if (breaks.TryGetValue((doctor.Id, date), out var dayBreaks) && dayBreaks.Any(item => start < item.End && end > item.Start))
                    continue;
                if (takenSlots.TryGetValue((doctor.Id, date), out var taken) && taken.Contains(start))
                    continue;
                yield return (windowStart, start);
            }
        }
    }

    /// <summary>The first free future slot for this patient on or after the date, skipping disruption days.</summary>
    private (DateOnly Date, int Start) ReserveSlot(DoctorCtx doctor, DateOnly from, PatientCtx patient)
    {
        if (from <= today)
            from = today.AddDays(1);
        for (var date = from; date <= from.AddDays(60); date = date.AddDays(1))
        {
            if (disruptions.ContainsKey((doctor.Id, date)))
                continue;
            foreach (var (_, start) in FreeSlotsWithWindow(doctor, date).ToList())
            {
                if (!IsPatientFree(patient, date, start, start + doctor.Duration))
                    continue;
                Take(doctor.Id, date, start);
                MarkBusy(patient, date, start, start + doctor.Duration);
                return (date, start);
            }
        }
        throw new InvalidOperationException($"No free slot found for {doctor.Spec.FullName} after {from:yyyy-MM-dd}.");
    }

    private DateOnly NextDate(DoctorCtx doctor, DateOnly from)
    {
        for (var date = from; date <= from.AddDays(30); date = date.AddDays(1))
            if (WindowsFor(doctor, date).Count > 0 && !disruptions.ContainsKey((doctor.Id, date)))
                return date;
        throw new InvalidOperationException($"{doctor.Spec.FullName} has no working day after {from:yyyy-MM-dd}.");
    }

    private DateOnly PreviousDate(DoctorCtx doctor, DateOnly from, Func<DateOnly, bool>? condition = null)
    {
        for (var date = from; date >= from.AddDays(-30); date = date.AddDays(-1))
            if (WindowsFor(doctor, date).Count > 0 && !disruptions.ContainsKey((doctor.Id, date)) && (condition is null || condition(date)))
                return date;
        throw new InvalidOperationException($"{doctor.Spec.FullName} has no working day before {from:yyyy-MM-dd}.");
    }

    private void Take(Guid doctorId, DateOnly date, int start)
    {
        if (!takenSlots.TryGetValue((doctorId, date), out var set))
            takenSlots[(doctorId, date)] = set = [];
        set.Add(start);
    }

    private bool IsPatientFree(PatientCtx patient, DateOnly date, int start, int end) =>
        !patientBusy.TryGetValue((patient.Entity.Id, date), out var list) || !list.Any(item => start < item.End + 30 && end + 30 > item.Start);

    private void MarkBusy(PatientCtx patient, DateOnly date, int start, int end)
    {
        if (!patientBusy.TryGetValue((patient.Entity.Id, date), out var list))
            patientBusy[(patient.Entity.Id, date)] = list = [];
        list.Add((start, end));
    }

    private PatientCtx? PickPatient(DoctorCtx doctor, DateOnly date, int start, int end) =>
        PickFor(doctor, candidate => IsPatientFree(candidate, date, start, end));

    private PatientCtx? PickWalkInPatient(DoctorCtx doctor, DateOnly date) =>
        PickFor(doctor, candidate => !patientBusy.ContainsKey((candidate.Entity.Id, date)));

    /// <summary>
    /// Picks a patient who fits the doctor: children see the paediatrician, adults see cardiology and internal medicine.
    /// Most visits go to patients whose file has a condition followed by this specialization, so their history reads as
    /// one story. The featured patient (index 0) only has the visits planned for her.
    /// </summary>
    private PatientCtx? PickFor(DoctorCtx doctor, Func<PatientCtx, bool> isFree)
    {
        var specialization = doctor.Spec.Specialization;
        if (!patientPools.TryGetValue(specialization, out var pools))
        {
            var eligible = patients.Skip(1).Where(patient => CanSee(specialization, patient)).ToArray();
            var followed = eligible.Where(patient => patient.Profile.ConditionFor(specialization) is not null).ToArray();
            patientPools[specialization] = pools = (eligible, followed);
        }

        if (pools.Followed.Length > 0 && Chance(0.75))
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var candidate = pools.Followed[rng.Next(pools.Followed.Length)];
                if (isFree(candidate))
                    return candidate;
            }

        for (var attempt = 0; attempt < 30 && pools.Eligible.Length > 0; attempt++)
        {
            var candidate = pools.Eligible[rng.Next(pools.Eligible.Length)];
            if (isFree(candidate))
                return candidate;
        }
        return null;
    }

    private static bool CanSee(string specialization, PatientCtx patient) => specialization switch
    {
        "Pediatrics" => patient.Profile.Child,
        "Cardiology" or "Internal Medicine" => !patient.Profile.Child,
        _ => true
    };

    // ---------------------------------------------------------------- small helpers

    private DateTimeOffset At(DateOnly date, int minutes) => clock.ToUtc(date, T(minutes));

    private static TimeOnly T(int minutes) => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes));

    private static string When(DateOnly date, int minutes) => $"{date:yyyy-MM-dd} at {T(minutes):h\\:mm tt}";

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a <= b ? a : b;

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a >= b ? a : b;

    private bool Chance(double probability) => rng.NextDouble() < probability;

    private T Pick<T>(IReadOnlyList<T> items) => items[rng.Next(items.Count)];

    // ---------------------------------------------------------------- working types

    private sealed class DoctorCtx(DemoDoctorSpec spec, Doctor entity, Guid userId)
    {
        public DemoDoctorSpec Spec { get; } = spec;
        public Doctor Entity { get; } = entity;
        public Guid UserId { get; } = userId;
        public Guid Id => Entity.Id;
        public int Duration => Spec.AppointmentDurationMinutes;
        public List<(DateOnly Start, DateOnly End)> Holidays { get; } = [];
        public DateOnly? LastWorkingDate { get; set; }
    }

    private sealed class PatientCtx(Patient entity, Guid userId, DemoPatientProfile profile)
    {
        public Patient Entity { get; } = entity;
        public Guid UserId { get; } = userId;
        public DemoPatientProfile Profile { get; } = profile;
        /// <summary>How many visit records each specialization has written so far, to walk the condition's visits in order.</summary>
        public Dictionary<string, int> RecordsBySpecialization { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>The first visit record carries the intake history; later records do not repeat it.</summary>
        public bool HistoryRecorded { get; set; }
    }

    private sealed class DisruptionPlan(DoctorCtx doctor, DateOnly date, int fromMinute, DoctorDisruption entity, bool past)
    {
        public DoctorCtx Doctor { get; } = doctor;
        public DateOnly Date { get; } = date;
        public int FromMinute { get; } = fromMinute;
        public DoctorDisruption Entity { get; } = entity;
        public bool Past { get; } = past;
        public List<Visit> Affected { get; } = [];
    }

    private sealed record ForcedVisit(PatientCtx Patient, Outcome Outcome, DemoClinicalCase? Case, FeaturedFile File, string? Addendum);

    private enum FeaturedFile { None, LabReport, SkinPhoto, Ecg }

    private enum Outcome
    {
        Completed,
        NoShow,
        CancelledByPatient,
        CancelledByClinic,
        Rejected,
        LeftBeforeConsultation,
        Disrupted,
        Upcoming,
        WalkInRejected
    }

    private sealed class Visit(DoctorCtx doctor, DateOnly date, PatientCtx patient, Outcome outcome)
    {
        public DoctorCtx Doctor { get; } = doctor;
        public DateOnly Date { get; } = date;
        public PatientCtx Patient { get; } = patient;
        public Outcome Outcome { get; } = outcome;
        public ForcedVisit? Forced { get; init; }
        public bool WalkIn { get; init; }
        public int StartMinute { get; init; }
        public int EndMinute { get; init; }
        public int WindowStartMinute { get; init; }
        public long Sequence { get; set; }
        public DateTimeOffset Arrival { get; set; }
        public DateTimeOffset Ready { get; set; }
        public DateTimeOffset? ApprovalRequested { get; init; }
        public DateTimeOffset? ApprovalDecided { get; init; }
        public DateTimeOffset? Called { get; set; }
        public DateTimeOffset? Started { get; set; }
        public DateTimeOffset? Completed { get; set; }
        public Appointment? Appointment { get; set; }
    }
}
