using MediCare.Application.Abstractions;
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

        services.AddScoped<IAppointmentRepository, EfAppointmentRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<AppointmentService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(clinicTimeZone);
        services.AddSingleton<IClinicClock, SystemClinicClock>();

        return services;
    }
}
