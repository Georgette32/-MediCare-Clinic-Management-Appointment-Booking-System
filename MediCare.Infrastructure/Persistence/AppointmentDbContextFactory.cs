using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MediCare.Infrastructure.Persistence;

public sealed class AppointmentDbContextFactory : IDesignTimeDbContextFactory<AppointmentDbContext>
{
    public AppointmentDbContext CreateDbContext(string[] args)
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var webDirectory = Path.Combine(currentDirectory, "MediCare.Web");
        var contentRoot = File.Exists(Path.Combine(webDirectory, "appsettings.json")) ? webDirectory : currentDirectory;
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var configuration = new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connection = configuration.GetConnectionString("MediCare")
            ?? throw new InvalidOperationException("Connection string 'MediCare' is missing. Configure it in appsettings or through ConnectionStrings__MediCare.");
        var options = new DbContextOptionsBuilder<AppointmentDbContext>().UseSqlServer(connection).Options;
        return new AppointmentDbContext(options);
    }
}
