using MediCare.Application;
using MediCare.Infrastructure.Persistence;
using MediCare.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediCare.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMediCareInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("MediCare")
            ?? throw new InvalidOperationException("Connection string 'MediCare' is missing. Configure it in appsettings or through ConnectionStrings__MediCare.");
        services.AddDbContext<AppointmentDbContext>(options => options.UseSqlServer(connection));
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<IAppointmentNotifier, LocalAppointmentNotifier>();
        return services;
    }
}
