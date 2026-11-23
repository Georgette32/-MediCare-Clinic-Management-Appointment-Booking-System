using FluentValidation;
using MediCare.Application.Interfaces.Services;
using MediCare.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MediCare.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<AuthService>(includeInternalTypes: true);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IDoctorManagementService, DoctorManagementService>();
        services.AddScoped<ISpecializationService, SpecializationService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IPatientProfileService, PatientProfileService>();
        services.AddScoped<IMedicalRecordService, MedicalRecordService>();
        services.AddScoped<IPrescriptionService, PrescriptionService>();
        services.AddScoped<IMedicalDocumentService, MedicalDocumentService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IFinanceService, FinanceService>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddScoped<IAppointmentReminderService, AppointmentReminderService>();

        services.AddScoped<AppointmentService>();
        services.AddScoped<ICheckInService, CheckInService>();
        services.AddScoped<IExpectedDurationService, ExpectedDurationService>();
        services.AddScoped<IGracePeriodService, GracePeriodService>();
        services.AddScoped<IWalkInAdmissionApprovalService, WalkInAdmissionApprovalService>();

        return services;
    }
}
