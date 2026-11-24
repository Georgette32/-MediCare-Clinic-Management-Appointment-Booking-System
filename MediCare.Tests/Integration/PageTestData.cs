using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Infrastructure.Identity;
using MediCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace MediCare.Tests.Integration;

public sealed record SeededUser(Guid UserId, string UserName, string Password);
public sealed record SeededDoctor(Guid UserId, Guid DoctorId, string UserName, string Password);
public sealed record SeededPatient(Guid UserId, Guid PatientId, string PhoneNumber, string Password);

/// <summary>
/// Creates the accounts and domain rows the page tests need. Every helper makes unique names, phone numbers and
/// emails, so tests that share one factory do not interfere with each other.
/// </summary>
public sealed class PageTestData(MediCareWebApplicationFactory factory)
{
    public const string UserPassword = "PageTests-User-Pa55word!";

    private static readonly DayOfWeek[] AllDays =
    [
        DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday
    ];

    public DateOnly ClinicToday => Clock.Today;
    public DateOnly ClinicTomorrow => Clock.Today.AddDays(1);

    private IClinicClock Clock => factory.Services.GetRequiredService<IClinicClock>();

    /// <summary>An Egyptian mobile number in local form (01x...) and its canonical +20 form.</summary>
    public static (string Local, string Canonical) NewEgyptianPhone()
    {
        var suffix = Random.Shared.Next(0, 100_000_000).ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
        // National number 10xxxxxxxx: the second digit must be one of 0, 1, 2, 5 for the Egyptian mobile ranges.
        return ("010" + suffix, "+2010" + suffix);
    }

    public async Task<SeededUser> CreateReceptionistAsync()
    {
        var email = $"reception-{Guid.NewGuid():N}@example.invalid";
        var user = await CreateIdentityUserAsync(email, email, "Reception Staff", MediCareRoles.Receptionist);
        return new SeededUser(user.Id, email, UserPassword);
    }

    public async Task<SeededDoctor> CreateDoctorAsync(string? fullName = null, int appointmentDurationMinutes = 20)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediCareDbContext>();

        var specialization = new Specialization(Guid.NewGuid(), "Page tests " + Guid.NewGuid().ToString("N"));
        context.Specializations.Add(specialization);

        var email = $"doctor-{Guid.NewGuid():N}@example.invalid";
        var name = fullName ?? "Dr Page " + Guid.NewGuid().ToString("N")[..6];
        var user = await CreateIdentityUserAsync(email, email, name, MediCareRoles.Doctor, scope.ServiceProvider);

        var doctor = new Doctor(Guid.NewGuid(), user.Id, name, specialization.Id, appointmentDurationMinutes);
        context.Doctors.Add(doctor);
        // A long working window on every day of the week, so any date the tests use is inside a working day.
        context.DoctorWorkingHours.AddRange(AllDays.Select(day =>
            DoctorWorkingHour.Create(doctor.Id, day, new TimeOnly(0, 0), new TimeOnly(23, 40))));
        await context.SaveChangesAsync();
        return new SeededDoctor(user.Id, doctor.Id, email, UserPassword);
    }

    public async Task<SeededPatient> CreatePatientAsync(string? fullName = null)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
        var (local, canonical) = NewEgyptianPhone();
        var name = fullName ?? "Patient " + Guid.NewGuid().ToString("N")[..6];
        var user = await CreateIdentityUserAsync(canonical, null, name, MediCareRoles.Patient, scope.ServiceProvider);

        var patient = new Patient(Guid.NewGuid(), user.Id, name, canonical);
        context.Patients.Add(patient);
        await context.SaveChangesAsync();
        return new SeededPatient(user.Id, patient.Id, local, UserPassword);
    }

    public async Task<Guid> CreateConfirmedAppointmentAsync(Guid patientId, Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
        var now = Clock.UtcNow;
        var appointment = Appointment.Request(patientId, doctorId, date, start, end, now.AddMinutes(-30));
        appointment.Confirm(now.AddMinutes(-20));
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();
        return appointment.Id;
    }

    /// <summary>A scheduled queue entry for today that the doctor has called and started (InConsultation).</summary>
    public async Task<Guid> CreateInConsultationEntryAsync(Guid patientId, Guid doctorId, TimeOnly slotStart)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
        var now = Clock.UtcNow;
        var today = Clock.Today;
        var appointment = Appointment.Request(patientId, doctorId, today, slotStart, slotStart.AddMinutes(20), now.AddHours(-2));
        appointment.Confirm(now.AddHours(-2).AddMinutes(1));
        context.Appointments.Add(appointment);

        var entry = QueueEntry.CreateScheduled(patientId, doctorId, appointment.Id, today, now.AddMinutes(-10));
        context.QueueEntries.Add(entry);
        await context.SaveChangesAsync();

        entry.Call(now.AddMinutes(-5));
        entry.StartConsultation(now.AddMinutes(-4));
        await context.SaveChangesAsync();
        return entry.Id;
    }

    public async Task AddNotificationAsync(Guid recipientUserId, string title, DateTimeOffset createdAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
        context.Notifications.Add(Notification.Create(recipientUserId, "page-test", title, "Seeded by the page tests.", null, null, null, createdAtUtc));
        await context.SaveChangesAsync();
    }

    /// <summary>Reads a value through the same database the host uses.</summary>
    public async Task<T> QueryAsync<T>(Func<MediCareDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<MediCareDbContext>());
    }

    private async Task<ApplicationUser> CreateIdentityUserAsync(
        string userName,
        string? email,
        string fullName,
        string role,
        IServiceProvider? provider = null)
    {
        var scoped = provider is null ? factory.Services.CreateScope() : null;
        try
        {
            var services = provider ?? scoped!.ServiceProvider;
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                Email = email,
                FullName = fullName,
                EmailConfirmed = email is not null,
                IsActive = true,
                MustChangePassword = false
            };

            var created = await users.CreateAsync(user, UserPassword);
            if (!created.Succeeded)
                throw new InvalidOperationException("Test user could not be created: " + string.Join(", ", created.Errors.Select(error => error.Description)));
            var added = await users.AddToRoleAsync(user, role);
            if (!added.Succeeded)
                throw new InvalidOperationException("Test user could not be added to " + role + ": " + string.Join(", ", added.Errors.Select(error => error.Description)));
            return user;
        }
        finally
        {
            scoped?.Dispose();
        }
    }
}
