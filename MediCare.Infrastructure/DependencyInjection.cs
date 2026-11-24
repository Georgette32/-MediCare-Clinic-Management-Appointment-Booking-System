using MediCare.Application.Common;
using MediCare.Application.Services;
using MediCare.Application.Abstractions;
using MediCare.Infrastructure.Identity;
using MediCare.Infrastructure.Maintenance;
using MediCare.Infrastructure.Notifications;
using MediCare.Infrastructure.Persistence;
using MediCare.Infrastructure.Storage;
using MediCare.Infrastructure.Time;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MediCare.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMediCare(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MediCare")
            ?? throw new InvalidOperationException("Connection string 'MediCare' is required.");
        var clinicTimeZoneId = configuration["Clinic:TimeZoneId"]
            ?? throw new InvalidOperationException("Clinic:TimeZoneId must be configured.");
        var clinicTimeZone = ClinicTimeZoneResolver.Resolve(clinicTimeZoneId);

        services.AddDbContext<MediCareDbContext>(options => options.UseSqlServer(connectionString));
        services.AddMediCareIdentity();

        services.Configure<ExpectedDurationOptions>(configuration.GetSection("Clinic:ExpectedDuration"));
        services.AddOptions<WalkInAdmissionApprovalOptions>()
            .Bind(configuration.GetSection("Clinic:WalkInAdmissionApproval"))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<DisruptionMaintenanceOptions>()
            .Bind(configuration.GetSection("Clinic:Maintenance"))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHostedService<DisruptionMaintenanceService>();
        services.AddScoped<IAppointmentRepository, EfAppointmentRepository>();
        services.AddScoped<IDoctorDisruptionRepository, EfDoctorDisruptionRepository>();
        services.AddScoped<IDisruptionService, DisruptionService>();
        services.AddScoped<ICheckInWarningProvider, DisruptionCheckInWarningProvider>();
        services.AddScoped<INotificationRepository, EfNotificationRepository>();
        services.AddScoped<INotificationProvider, InAppNotificationProvider>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IPatientNotificationPublisher, SafePatientNotificationPublisher>();
        services.AddScoped<IStaffNotificationPublisher, SafeStaffNotificationPublisher>();
        services.AddScoped<IAppointmentReminderRepository, EfAppointmentReminderRepository>();
        services.AddScoped<IAppointmentReminderService, AppointmentReminderService>();
        services.AddScoped<IMedicalAttachmentRepository, EfMedicalAttachmentRepository>();
        services.AddScoped<IMedicalAttachmentService, MedicalAttachmentService>();
        services.AddOptions<MedicalFileOptions>()
            .Bind(configuration.GetSection(MedicalFileOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MedicalFileOptions>, MedicalFileOptionsValidator>();
        services.AddSingleton<IMedicalFileStore>(provider =>
        {
            var environment = provider.GetRequiredService<IWebHostEnvironment>();
            var medicalFiles = provider.GetRequiredService<IOptions<MedicalFileOptions>>().Value;
            return new FileSystemMedicalFileStore(
                MedicalFileStorageLocation.Resolve(medicalFiles.StoragePath, environment.ContentRootPath, environment.WebRootPath));
        });
        services.AddScoped<IQueueRepository, EfQueueRepository>();
        services.AddScoped<IWalkInAdmissionApprovalRepository, EfWalkInAdmissionApprovalRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IIdentityAccountStore, IdentityAccountStore>();
        services.AddScoped<IAccountProvisioningRepository, EfAccountProvisioningRepository>();
        services.AddScoped<IAccountProvisioningService, AccountProvisioningService>();
        services.AddScoped<AppointmentService>();
        services.AddScoped<IAppointmentBookingFlow, AppointmentBookingFlow>();
        services.AddScoped<IAppointmentQueryRepository, EfAppointmentQueryRepository>();
        services.AddScoped<IAppointmentQueryService, AppointmentQueryService>();
        services.AddScoped<IAccountProfileRepository, EfAccountProfileRepository>();
        services.AddScoped<IIdentityProfileStore, IdentityProfileStore>();
        services.AddScoped<IAccountProfileService, AccountProfileService>();
        services.AddScoped<ICheckInService, CheckInService>();
        services.AddScoped<IWalkInAdmissionApprovalService, WalkInAdmissionApprovalService>();
        services.AddScoped<IExpectedDurationService, ExpectedDurationService>();
        services.AddScoped<IGracePeriodService, GracePeriodService>();
        services.AddScoped<IDoctorCallWindowService, DoctorCallWindowService>();
        services.AddScoped<IClinicMaintenanceTasks, ClinicMaintenanceTasks>();
        services.AddScoped<IMedicalRecordRepository, EfMedicalRecordRepository>();
        services.AddScoped<IMedicalRecordService, MedicalRecordService>();
        services.AddScoped<IPreviousDayAppointmentRepository, EfPreviousDayAppointmentRepository>();
        services.AddScoped<IPreviousDayAppointmentResolver, PreviousDayAppointmentResolver>();
        services.AddScoped<IDoctorScheduleRepository, EfDoctorScheduleRepository>();
        services.AddScoped<IDoctorAvailabilityService, DoctorAvailabilityService>();
        services.AddScoped<IWorkingHourExtensionService, WorkingHourExtensionService>();
        services.AddScoped<IQueueBoardRepository, EfQueueBoardRepository>();
        services.AddScoped<IReceptionQueueService, ReceptionQueueService>();
        services.AddScoped<IConsultationService, ConsultationService>();
        services.AddScoped<IQueueBoardService, QueueBoardService>();
        services.AddScoped<IDoctorScheduleManagementRepository, EfDoctorScheduleManagementRepository>();
        services.AddScoped<IDoctorScheduleService, DoctorScheduleService>();
        services.AddScoped<IDoctorDirectoryService, DoctorDirectoryService>();
        services.AddScoped<ISpecializationAdministrationRepository, EfSpecializationAdministrationRepository>();
        services.AddScoped<ISpecializationAdministrationService, SpecializationAdministrationService>();
        services.AddScoped<IStaffAccountRepository, EfStaffAccountRepository>();
        services.AddScoped<IDoctorDeactivationService, DoctorDeactivationService>();
        services.AddScoped<IStaffAccountService, StaffAccountService>();
        services.AddScoped<IClinicSettingsRepository, EfClinicSettingsRepository>();
        services.AddScoped<ClinicSettingsService>();
        services.AddScoped<IClinicSettingsService>(provider => provider.GetRequiredService<ClinicSettingsService>());
        services.AddScoped<IClinicSettingsProvider>(provider => provider.GetRequiredService<ClinicSettingsService>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(clinicTimeZone);
        services.AddSingleton<IClinicClock, SystemClinicClock>();

        return services;
    }

    /// <summary>
    /// Identity registration shared by production and integration tests so the two cannot drift.
    /// Email is optional (Reception-created and phone-only patients have none); uniqueness of non-null
    /// emails is enforced by the filtered unique EmailIndex in MediCareDbContext and by the service layer.
    /// </summary>
    public static IServiceCollection AddMediCareIdentity(this IServiceCollection services)
    {
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<MediCareDbContext>()
            .AddDefaultTokenProviders();

        // Explicit paths so a signed-in user without the role gets the friendly access-denied page, not a blank 403.
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
        });
        return services;
    }
}
