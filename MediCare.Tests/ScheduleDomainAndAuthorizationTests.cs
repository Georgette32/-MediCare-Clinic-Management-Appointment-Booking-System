using System.Reflection;
using MediCare.Domain.Entities;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Tests;

public sealed class ScheduleDomainTests
{
    [Fact]
    public void Biography_is_trimmed_blank_clears_and_limit_is_1000()
    {
        var doctor = new Doctor(Guid.NewGuid(), Guid.NewGuid(), "Dr. Bio", Guid.NewGuid(), 20);

        doctor.UpdateBiography("  Heart care  ");
        Assert.Equal("Heart care", doctor.Biography);

        doctor.UpdateBiography("   ");
        Assert.Null(doctor.Biography);

        doctor.UpdateBiography(new string('a', Doctor.MaxBiographyLength));
        Assert.Equal(Doctor.MaxBiographyLength, doctor.Biography!.Length);

        Assert.Throws<ArgumentException>(() => doctor.UpdateBiography(new string('a', Doctor.MaxBiographyLength + 1)));
    }

    [Fact]
    public void Appointment_duration_must_be_positive()
    {
        var doctor = new Doctor(Guid.NewGuid(), Guid.NewGuid(), "Dr. Duration", Guid.NewGuid(), 20);

        Assert.Throws<ArgumentOutOfRangeException>(() => doctor.ChangeAppointmentDuration(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => doctor.ChangeAppointmentDuration(-5));
        doctor.ChangeAppointmentDuration(30);
        Assert.Equal(30, doctor.AppointmentDurationMinutes);
    }

    [Fact]
    public void Scheduled_duration_applies_only_from_its_effective_date()
    {
        var doctor = new Doctor(Guid.NewGuid(), Guid.NewGuid(), "Dr. Schedule", Guid.NewGuid(), 30);
        var today = new DateOnly(2026, 11, 28);
        var effective = new DateOnly(2026, 11, 29);

        doctor.ScheduleDurationChange(35, effective, today);

        Assert.Equal(30, doctor.DurationOn(today));
        Assert.Equal(30, doctor.DurationOn(effective.AddDays(-1)));
        Assert.Equal(35, doctor.DurationOn(effective));
        Assert.Equal(35, doctor.DurationOn(effective.AddDays(3)));
        Assert.Equal(30, doctor.AppointmentDurationMinutes);
        Assert.Equal(35, doctor.NextAppointmentDurationMinutes);
        Assert.Equal(effective, doctor.NextDurationEffectiveFrom);
    }

    [Fact]
    public void Scheduled_duration_must_take_effect_after_clinic_today()
    {
        var doctor = new Doctor(Guid.NewGuid(), Guid.NewGuid(), "Dr. Schedule", Guid.NewGuid(), 30);
        var today = new DateOnly(2026, 11, 28);

        Assert.Throws<ArgumentOutOfRangeException>(() => doctor.ScheduleDurationChange(35, today, today));
        Assert.Throws<ArgumentOutOfRangeException>(() => doctor.ScheduleDurationChange(35, today.AddDays(-1), today));
        Assert.Null(doctor.NextAppointmentDurationMinutes);
        Assert.Null(doctor.NextDurationEffectiveFrom);
    }

    [Fact]
    public void Scheduling_the_duration_already_in_force_clears_the_pending_change()
    {
        var doctor = new Doctor(Guid.NewGuid(), Guid.NewGuid(), "Dr. Schedule", Guid.NewGuid(), 30);
        var today = new DateOnly(2026, 11, 28);
        var effective = new DateOnly(2026, 11, 29);
        doctor.ScheduleDurationChange(35, effective, today);

        doctor.ScheduleDurationChange(30, effective, today);

        Assert.Null(doctor.NextAppointmentDurationMinutes);
        Assert.Null(doctor.NextDurationEffectiveFrom);
        Assert.Equal(30, doctor.DurationOn(effective));
    }

    [Fact]
    public void Scheduling_again_replaces_the_pending_change()
    {
        var doctor = new Doctor(Guid.NewGuid(), Guid.NewGuid(), "Dr. Schedule", Guid.NewGuid(), 30);
        var today = new DateOnly(2026, 11, 28);
        doctor.ScheduleDurationChange(35, new DateOnly(2026, 11, 29), today);

        doctor.ScheduleDurationChange(40, new DateOnly(2026, 12, 2), today);

        Assert.Equal(40, doctor.NextAppointmentDurationMinutes);
        Assert.Equal(new DateOnly(2026, 12, 2), doctor.NextDurationEffectiveFrom);
        Assert.Equal(30, doctor.DurationOn(new DateOnly(2026, 12, 1)));
        Assert.Equal(40, doctor.DurationOn(new DateOnly(2026, 12, 2)));
    }

    [Fact]
    public void Cancelling_a_scheduled_duration_restores_the_current_duration()
    {
        var doctor = new Doctor(Guid.NewGuid(), Guid.NewGuid(), "Dr. Schedule", Guid.NewGuid(), 30);
        var effective = new DateOnly(2026, 11, 29);
        doctor.ScheduleDurationChange(35, effective, new DateOnly(2026, 11, 28));

        doctor.CancelScheduledDurationChange();

        Assert.Null(doctor.NextAppointmentDurationMinutes);
        Assert.Null(doctor.NextDurationEffectiveFrom);
        Assert.Equal(30, doctor.DurationOn(effective));
    }

    [Fact]
    public void Scheduled_duration_is_promoted_only_on_or_after_its_effective_date()
    {
        var doctor = new Doctor(Guid.NewGuid(), Guid.NewGuid(), "Dr. Schedule", Guid.NewGuid(), 30);
        doctor.ScheduleDurationChange(35, new DateOnly(2026, 11, 29), new DateOnly(2026, 11, 28));

        Assert.False(doctor.PromoteDueDurationChange(new DateOnly(2026, 11, 28)));
        Assert.Equal(30, doctor.AppointmentDurationMinutes);
        Assert.Equal(35, doctor.NextAppointmentDurationMinutes);

        Assert.True(doctor.PromoteDueDurationChange(new DateOnly(2026, 11, 29)));
        Assert.Equal(35, doctor.AppointmentDurationMinutes);
        Assert.Null(doctor.NextAppointmentDurationMinutes);
        Assert.Null(doctor.NextDurationEffectiveFrom);
        Assert.False(doctor.PromoteDueDurationChange(new DateOnly(2026, 11, 30)));
    }

    [Fact]
    public void Working_window_times_must_be_a_valid_range()
    {
        var window = DoctorWorkingHour.Create(Guid.NewGuid(), DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(11, 0));

        Assert.Throws<ArgumentException>(() => window.ChangeTimes(new TimeOnly(11, 0), new TimeOnly(9, 0)));
        window.ChangeTimes(new TimeOnly(8, 0), new TimeOnly(12, 0));
        Assert.Equal(new TimeOnly(12, 0), window.EndTime);
    }

    [Theory]
    [InlineData(9, 11, 10, 12, true)]
    [InlineData(9, 11, 11, 12, false)]
    [InlineData(9, 11, 8, 9, false)]
    [InlineData(9, 11, 9, 11, true)]
    public void Overlap_treats_ranges_as_half_open(int startA, int endA, int startB, int endB, bool expected)
    {
        Assert.Equal(expected, DoctorWorkingHour.Overlaps(new TimeOnly(startA, 0), new TimeOnly(endA, 0), new TimeOnly(startB, 0), new TimeOnly(endB, 0)));
    }

    [Fact]
    public void Specialization_name_is_trimmed_and_limited_to_120()
    {
        var specialization = new Specialization(Guid.NewGuid(), "  Pediatrics  ", "  Children  ");
        Assert.Equal("Pediatrics", specialization.Name);
        Assert.Equal("Children", specialization.Description);

        specialization.Update("Paediatrics", null);
        Assert.Equal("Paediatrics", specialization.Name);
        Assert.Null(specialization.Description);

        Assert.Throws<ArgumentException>(() => specialization.Update(new string('x', Specialization.MaxNameLength + 1), null));
        Assert.Throws<ArgumentException>(() => specialization.Update("  ", null));
    }

    [Fact]
    public void Clinic_sample_size_accepts_null_or_positive_only()
    {
        var settings = ClinicSettings.CreateEmpty(DateTimeOffset.UtcNow);
        var actor = Guid.NewGuid();

        settings.SetMinimumConsultationSampleSize(5, actor, DateTimeOffset.UtcNow);
        Assert.Equal(5, settings.MinimumConsultationSampleSize);
        Assert.Equal(actor, settings.UpdatedByUserId);

        Assert.Throws<ArgumentOutOfRangeException>(() => settings.SetMinimumConsultationSampleSize(0, actor, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => settings.SetMinimumConsultationSampleSize(3, Guid.Empty, DateTimeOffset.UtcNow));

        settings.SetMinimumConsultationSampleSize(null, actor, DateTimeOffset.UtcNow);
        Assert.Null(settings.MinimumConsultationSampleSize);
    }
}

public sealed class ScheduleAuthorizationTests
{
    [Fact]
    public void Schedule_management_is_limited_to_doctors_and_admins()
    {
        var authorization = typeof(ScheduleManagementController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.Equal($"{MediCareRoles.Doctor},{MediCareRoles.Admin}", authorization!.Roles);
    }

    [Fact]
    public void Admin_schedule_and_settings_pages_are_admin_only()
    {
        var authorization = typeof(AdminController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.Equal(MediCareRoles.Admin, authorization!.Roles);
    }

    [Fact]
    public void Doctor_schedule_page_is_doctor_only()
    {
        var authorization = typeof(DoctorController).GetCustomAttribute<AuthorizeAttribute>();
        var action = typeof(DoctorController).GetMethod(nameof(DoctorController.Schedule), Type.EmptyTypes)
                     ?? typeof(DoctorController).GetMethods().Single(method => method.Name == nameof(DoctorController.Schedule));

        Assert.NotNull(authorization);
        Assert.Equal(MediCareRoles.Doctor, authorization!.Roles);
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
    }

    [Fact]
    public void Doctor_discovery_requires_a_signed_in_user_for_every_role()
    {
        var authorization = typeof(DoctorsController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.True(string.IsNullOrEmpty(authorization!.Roles));
        Assert.Null(typeof(DoctorsController).GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData(typeof(ScheduleManagementController))]
    [InlineData(typeof(AdminController))]
    public void Every_post_action_requires_antiforgery(Type controller)
    {
        var posts = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<HttpPostAttribute>() is not null)
            .ToArray();

        Assert.NotEmpty(posts);
        foreach (var post in posts)
            Assert.NotNull(post.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void Admin_actions_are_not_anonymous()
    {
        Assert.Null(typeof(AdminController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(typeof(ScheduleManagementController).GetCustomAttribute<AllowAnonymousAttribute>());
        foreach (var method in typeof(AdminController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
    }
}
