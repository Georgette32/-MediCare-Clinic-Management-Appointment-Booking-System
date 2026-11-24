using MediCare.Application.Abstractions;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Identity;

namespace MediCare.Tests;

public sealed class ScheduleDiscoveryAndAdministrationTests
{
    private static readonly DateOnly Today = new(2026, 12, 1);
    private static readonly DateTimeOffset Now = new(2026, 12, 1, 8, 0, 0, TimeSpan.Zero);

    // ─── discovery ─────────────────────────────────────────────────────────

    private sealed class DiscoveryHarness
    {
        public required ScheduleWorld World { get; init; }
        public required DoctorDirectoryService Directory { get; init; }
    }

    private static DiscoveryHarness CreateDiscovery()
    {
        var world = new ScheduleWorld();
        var clock = new FixedClock(Now, Today);
        var availability = new DoctorAvailabilityService(world, world, clock);
        return new DiscoveryHarness
        {
            World = world,
            Directory = new DoctorDirectoryService(world, world, availability, clock)
        };
    }

    private static Doctor AddDoctor(ScheduleWorld world, string name, Guid specializationId, int duration = 20, string? biography = null, bool active = true)
    {
        var doctor = new Doctor(Guid.NewGuid(), Guid.NewGuid(), name, specializationId, duration, active);
        doctor.UpdateBiography(biography);
        world.Doctors.Add(doctor);
        return doctor;
    }

    [Fact]
    public async Task Search_filters_by_specialization_and_name_and_hides_inactive_doctors()
    {
        var h = CreateDiscovery();
        var cardiology = Guid.NewGuid();
        var dermatology = Guid.NewGuid();
        AddDoctor(h.World, "Mona Adel", cardiology);
        AddDoctor(h.World, "Omar Hassan", dermatology);
        AddDoctor(h.World, "Mona Ziad", dermatology, active: false);

        var bySpecialization = await h.Directory.SearchAsync(dermatology, null, null, CancellationToken.None);
        var byName = await h.Directory.SearchAsync(null, "mona", null, CancellationToken.None);

        Assert.Equal(new[] { "Omar Hassan" }, bySpecialization.Select(item => item.FullName));
        Assert.Equal(new[] { "Mona Adel" }, byName.Select(item => item.FullName));
    }

    [Fact]
    public async Task Search_orders_doctors_by_name()
    {
        var h = CreateDiscovery();
        var specialization = Guid.NewGuid();
        AddDoctor(h.World, "Zeina", specialization);
        AddDoctor(h.World, "Amr", specialization);

        var results = await h.Directory.SearchAsync(null, null, null, CancellationToken.None);

        Assert.Equal(new[] { "Amr", "Zeina" }, results.Select(item => item.FullName));
    }

    [Fact]
    public async Task Search_with_a_date_reports_free_slots_for_that_day()
    {
        var h = CreateDiscovery();
        var doctor = AddDoctor(h.World, "Layla", Guid.NewGuid(), duration: 20);
        h.World.WorkingHours.Add(DoctorWorkingHour.Create(doctor.Id, Today.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(10, 0)));
        h.World.Appointments.Add(new ScheduleWorld.ScheduleAppointment(doctor.Id, Today, new TimeOnly(9, 0), new TimeOnly(9, 20), AppointmentStatus.Confirmed));

        var results = await h.Directory.SearchAsync(null, null, Today, CancellationToken.None);

        var item = Assert.Single(results);
        Assert.Equal(2, item.FreeSlots);
        Assert.False(item.IsHoliday);
    }

    [Fact]
    public async Task Search_marks_holiday_dates_as_unavailable_instead_of_counting_slots()
    {
        var h = CreateDiscovery();
        var doctor = AddDoctor(h.World, "Hala", Guid.NewGuid());
        h.World.WorkingHours.Add(DoctorWorkingHour.Create(doctor.Id, Today.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(10, 0)));
        h.World.Holidays.Add(DoctorHoliday.Create(doctor.Id, Today, Today, "Leave"));

        var item = Assert.Single(await h.Directory.SearchAsync(null, null, Today, CancellationToken.None));

        Assert.True(item.IsHoliday);
        Assert.Null(item.FreeSlots);
    }

    [Fact]
    public async Task Biography_excerpt_is_truncated_for_listing()
    {
        var h = CreateDiscovery();
        AddDoctor(h.World, "Karim", Guid.NewGuid(), biography: new string('b', 300));

        var item = Assert.Single(await h.Directory.SearchAsync(null, null, null, CancellationToken.None));

        Assert.NotNull(item.BiographyExcerpt);
        Assert.True(item.BiographyExcerpt!.EndsWith("...", StringComparison.Ordinal));
        Assert.True(item.BiographyExcerpt.Length <= DoctorDirectoryService.BiographyExcerptLength + 3);
    }

    [Fact]
    public async Task Profile_lists_weekly_hours_upcoming_holidays_and_selected_day_slots()
    {
        var h = CreateDiscovery();
        var doctor = AddDoctor(h.World, "Nadia", Guid.NewGuid(), duration: 30, biography: "Pediatrics");
        h.World.WorkingHours.Add(DoctorWorkingHour.Create(doctor.Id, Today.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(10, 0)));
        h.World.Holidays.Add(DoctorHoliday.Create(doctor.Id, Today.AddDays(10), Today.AddDays(12), "Conference"));
        h.World.Holidays.Add(DoctorHoliday.Create(doctor.Id, Today.AddDays(-20), Today.AddDays(-15), "Past"));

        var profile = await h.Directory.GetProfileAsync(doctor.Id, Today, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal("Pediatrics", profile!.Biography);
        Assert.Single(profile.WeeklyHours);
        Assert.Equal(new[] { new TimeOnly(9, 0), new TimeOnly(9, 30) }, profile.FreeSlots);
        Assert.Single(profile.UpcomingHolidays);
        Assert.Equal("Conference", profile.UpcomingHolidays[0].Reason);
    }

    [Fact]
    public async Task Profile_of_an_inactive_or_unknown_doctor_is_not_found()
    {
        var h = CreateDiscovery();
        var inactive = AddDoctor(h.World, "Sami", Guid.NewGuid(), active: false);

        Assert.Null(await h.Directory.GetProfileAsync(inactive.Id, Today, CancellationToken.None));
        Assert.Null(await h.Directory.GetProfileAsync(Guid.NewGuid(), Today, CancellationToken.None));
    }

    // ─── specialization administration ─────────────────────────────────────

    private sealed class FakeSpecializationRepository : ISpecializationAdministrationRepository
    {
        public List<Specialization> Items { get; } = [];
        public Dictionary<Guid, int> DoctorCounts { get; } = [];
        public List<Specialization> Removed { get; } = [];

        public Task<IReadOnlyList<SpecializationAdminRow>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SpecializationAdminRow>>(Items.Select(item =>
                new SpecializationAdminRow(item.Id, item.Name, item.Description, DoctorCounts.GetValueOrDefault(item.Id))).ToArray());

        public Task<Specialization?> GetTrackedAsync(Guid specializationId, CancellationToken ct) =>
            Task.FromResult(Items.FirstOrDefault(item => item.Id == specializationId));

        public Task<bool> NameExistsAsync(string name, Guid? exceptSpecializationId, CancellationToken ct) =>
            Task.FromResult(Items.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && item.Id != exceptSpecializationId));

        public Task<int> CountDoctorsAsync(Guid specializationId, CancellationToken ct) =>
            Task.FromResult(DoctorCounts.GetValueOrDefault(specializationId));

        public Task AddAsync(Specialization specialization, CancellationToken ct)
        {
            Items.Add(specialization);
            return Task.CompletedTask;
        }

        public void Remove(Specialization specialization)
        {
            Items.Remove(specialization);
            Removed.Add(specialization);
        }
    }

    [Fact]
    public async Task Specialization_names_must_be_unique_and_non_blank()
    {
        var repository = new FakeSpecializationRepository();
        repository.Items.Add(new Specialization(Guid.NewGuid(), "Cardiology"));
        var service = new SpecializationAdministrationService(repository, new InMemoryUnitOfWork());

        var duplicate = await service.CreateAsync("cardiology", null, CancellationToken.None);
        var blank = await service.CreateAsync("   ", null, CancellationToken.None);
        var created = await service.CreateAsync("  Dermatology  ", "Skin care", CancellationToken.None);

        Assert.False(duplicate.Succeeded);
        Assert.False(blank.Succeeded);
        Assert.True(created.Succeeded);
        Assert.Contains(repository.Items, item => item.Name == "Dermatology" && item.Description == "Skin care");
    }

    [Fact]
    public async Task Specialization_name_cannot_exceed_120_characters()
    {
        var repository = new FakeSpecializationRepository();
        var service = new SpecializationAdministrationService(repository, new InMemoryUnitOfWork());

        var result = await service.CreateAsync(new string('a', Specialization.MaxNameLength + 1), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(repository.Items);
    }

    [Fact]
    public async Task Specialization_delete_is_refused_while_a_doctor_references_it()
    {
        var repository = new FakeSpecializationRepository();
        var specialization = new Specialization(Guid.NewGuid(), "Neurology");
        repository.Items.Add(specialization);
        repository.DoctorCounts[specialization.Id] = 2;
        var service = new SpecializationAdministrationService(repository, new InMemoryUnitOfWork());

        var result = await service.DeleteAsync(specialization.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("used by 2 doctor(s)", result.Message);
        Assert.Single(repository.Items);
    }

    [Fact]
    public async Task Unused_specialization_can_be_deleted_and_renamed()
    {
        var repository = new FakeSpecializationRepository();
        var specialization = new Specialization(Guid.NewGuid(), "Oncology");
        repository.Items.Add(specialization);
        var service = new SpecializationAdministrationService(repository, new InMemoryUnitOfWork());

        var renamed = await service.UpdateAsync(specialization.Id, "Oncology Care", "Cancer care", CancellationToken.None);
        Assert.True(renamed.Succeeded);
        Assert.Equal("Oncology Care", specialization.Name);

        var deleted = await service.DeleteAsync(specialization.Id, CancellationToken.None);
        Assert.True(deleted.Succeeded);
        Assert.Empty(repository.Items);
    }

    // ─── staff activation ──────────────────────────────────────────────────

    private sealed class FakeStaffRepository : IStaffAccountRepository
    {
        public Dictionary<Guid, string> Roles { get; } = [];
        public Dictionary<Guid, bool> Active { get; } = [];
        public List<Doctor> Doctors { get; } = [];

        public Task<IReadOnlyList<StaffAccountRow>> ListStaffAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StaffAccountRow>>(Array.Empty<StaffAccountRow>());

        public Task<string?> GetRoleNameAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(Roles.TryGetValue(userId, out var role) ? role : null);

        public Task<bool> SetAccountActiveAsync(Guid userId, bool isActive, CancellationToken ct)
        {
            if (!Roles.ContainsKey(userId))
                return Task.FromResult(false);
            Active[userId] = isActive;
            foreach (var doctor in Doctors.Where(item => item.ApplicationUserId == userId))
            {
                if (isActive) doctor.Activate(); else doctor.Deactivate();
            }
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task Admin_cannot_deactivate_their_own_account()
    {
        var adminId = Guid.NewGuid();
        var staff = new FakeStaffRepository();
        staff.Roles[adminId] = MediCareRoles.Admin;
        var service = NewStaffService(staff);

        var result = await service.SetActiveAsync(adminId, false, adminId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("your own account", result.Message);
        Assert.False(staff.Active.ContainsKey(adminId));
    }

    [Fact]
    public async Task Admin_accounts_are_not_deactivated_here()
    {
        var adminId = Guid.NewGuid();
        var otherAdmin = Guid.NewGuid();
        var staff = new FakeStaffRepository();
        staff.Roles[otherAdmin] = MediCareRoles.Admin;
        var service = NewStaffService(staff);

        var result = await service.SetActiveAsync(otherAdmin, false, adminId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(staff.Active.ContainsKey(otherAdmin));
    }

    [Fact]
    public async Task Deactivating_a_doctor_also_hides_the_doctor_profile_and_reactivating_restores_it()
    {
        var adminId = Guid.NewGuid();
        var doctorUserId = Guid.NewGuid();
        var staff = new FakeStaffRepository();
        staff.Roles[doctorUserId] = MediCareRoles.Doctor;
        var doctor = new Doctor(Guid.NewGuid(), doctorUserId, "Dr. Staff", Guid.NewGuid(), 20);
        staff.Doctors.Add(doctor);
        var appointments = new InMemoryAppointmentRepository();
        appointments.AddDoctor(doctor);
        var service = NewStaffService(staff, appointments);

        Assert.True((await service.SetActiveAsync(doctorUserId, false, adminId, CancellationToken.None)).Succeeded);
        Assert.False(doctor.IsActive);
        Assert.True((await service.SetActiveAsync(doctorUserId, true, adminId, CancellationToken.None)).Succeeded);
        Assert.True(doctor.IsActive);
    }

    private static StaffAccountService NewStaffService(FakeStaffRepository staff, InMemoryAppointmentRepository? appointments = null)
    {
        appointments ??= new InMemoryAppointmentRepository();
        var clock = new FixedClock(QueueTestData.Noon, QueueTestData.Today);
        var queue = new InMemoryQueueRepository();
        var unitOfWork = new InMemoryUnitOfWork();
        var availability = new DoctorAvailabilityService(appointments, new InMemoryDoctorScheduleRepository(), clock);
        var appointmentService = new AppointmentService(appointments, unitOfWork, clock, queue, availability);
        var deactivation = new DoctorDeactivationService(appointments, queue, new InMemoryApprovalRepository(), staff,
            appointmentService, new RecordingPatientNotificationPublisher(), unitOfWork, clock);
        return new StaffAccountService(staff, unitOfWork, deactivation);
    }

    [Fact]
    public void Inactive_or_missing_accounts_cannot_hold_a_session()
    {
        Assert.True(AccountAccessPolicy.IsSessionAllowed(accountFound: true, isActive: true));
        Assert.False(AccountAccessPolicy.IsSessionAllowed(accountFound: true, isActive: false));
        Assert.False(AccountAccessPolicy.IsSessionAllowed(accountFound: false, isActive: true));
        Assert.False(AccountAccessPolicy.IsSessionAllowed(accountFound: false, isActive: false));
    }

    // ─── clinic settings ───────────────────────────────────────────────────

    private sealed class FakeClinicSettingsRepository : IClinicSettingsRepository
    {
        public ClinicSettings? Row { get; set; }

        public Task<ClinicSettings?> GetAsync(CancellationToken ct) => Task.FromResult(Row);

        public Task AddAsync(ClinicSettings settings, CancellationToken ct)
        {
            Row = settings;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Clinic_settings_provider_returns_null_when_nothing_is_set()
    {
        var repository = new FakeClinicSettingsRepository();
        var service = new ClinicSettingsService(repository, new InMemoryUnitOfWork(), new FixedClock(Now, Today));

        Assert.Null(await service.GetMinimumConsultationSampleSizeAsync(CancellationToken.None));
        var view = await service.GetAsync(CancellationToken.None);
        Assert.Null(view.MinimumConsultationSampleSize);
        Assert.Null(view.UpdatedAtUtc);
    }

    [Fact]
    public async Task Setting_the_sample_size_creates_the_single_row_and_the_provider_reads_it()
    {
        var repository = new FakeClinicSettingsRepository();
        var actor = Guid.NewGuid();
        var service = new ClinicSettingsService(repository, new InMemoryUnitOfWork(), new FixedClock(Now, Today));

        var result = await service.SetMinimumSampleSizeAsync(5, actor, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ClinicSettings.SingletonId, repository.Row!.Id);
        Assert.Equal(5, await service.GetMinimumConsultationSampleSizeAsync(CancellationToken.None));
        Assert.Equal(actor, repository.Row.UpdatedByUserId);
    }

    [Fact]
    public async Task Clearing_the_sample_size_returns_the_provider_to_unset()
    {
        var repository = new FakeClinicSettingsRepository();
        var service = new ClinicSettingsService(repository, new InMemoryUnitOfWork(), new FixedClock(Now, Today));
        await service.SetMinimumSampleSizeAsync(5, Guid.NewGuid(), CancellationToken.None);

        var result = await service.SetMinimumSampleSizeAsync(null, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(await service.GetMinimumConsultationSampleSizeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Sample_size_must_be_positive_when_set()
    {
        var repository = new FakeClinicSettingsRepository();
        var service = new ClinicSettingsService(repository, new InMemoryUnitOfWork(), new FixedClock(Now, Today));

        var result = await service.SetMinimumSampleSizeAsync(0, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(repository.Row);
    }

    [Fact]
    public void Settings_entity_keeps_its_fixed_singleton_id()
    {
        var settings = ClinicSettings.CreateEmpty(Now);

        Assert.Equal(1, settings.Id);
        Assert.Null(settings.MinimumConsultationSampleSize);
    }
}
