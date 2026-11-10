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

        return services;
    }
}
