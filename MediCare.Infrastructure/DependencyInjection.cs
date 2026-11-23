using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using MediCare.Application.Services;
using MediCare.Infrastructure.Identity;
using MediCare.Infrastructure.Persistence;
using MediCare.Infrastructure.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<MediCareDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<ExpectedDurationOptions>(configuration.GetSection("Clinic:ExpectedDuration"));
        services.AddOptions<WalkInAdmissionApprovalOptions>()
            .Bind(configuration.GetSection("Clinic:WalkInAdmissionApproval"))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IAppointmentRepository, EfAppointmentRepository>();
        services.AddScoped<IQueueRepository, EfQueueRepository>();
        services.AddScoped<IWalkInAdmissionApprovalRepository, EfWalkInAdmissionApprovalRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<AppointmentService>();
        services.AddScoped<ICheckInService, CheckInService>();
        services.AddScoped<IWalkInAdmissionApprovalService, WalkInAdmissionApprovalService>();
        services.AddScoped<IExpectedDurationService, ExpectedDurationService>();
        services.AddScoped<IGracePeriodService, GracePeriodService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(clinicTimeZone);
        services.AddSingleton<IClinicClock, SystemClinicClock>();

        return services;
    }
}
