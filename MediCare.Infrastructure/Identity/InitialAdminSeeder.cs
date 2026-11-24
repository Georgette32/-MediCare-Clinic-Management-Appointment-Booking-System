using MediCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediCare.Infrastructure.Identity;

public sealed record InitialAdminSettings(string Email, string Password, string FullName)
{
    public static InitialAdminSettings FromConfiguration(IConfiguration configuration)
    {
        var email = Required(configuration, "BootstrapAdmin:Email");
        var password = Required(configuration, "BootstrapAdmin:Password");
        var fullName = Required(configuration, "BootstrapAdmin:FullName");
        return new InitialAdminSettings(email.Trim(), password, fullName.Trim());
    }

    private static string Required(IConfiguration configuration, string key) =>
        !string.IsNullOrWhiteSpace(configuration[key])
            ? configuration[key]!
            : throw new InvalidOperationException($"Required secure bootstrap configuration '{key}' is missing.");
}

public static class InitialAdminSeeder
{
    private static readonly string[] Roles =
    [
        MediCareRoles.Admin,
        MediCareRoles.Doctor,
        MediCareRoles.Receptionist,
        MediCareRoles.Patient
    ];

    public static async Task SeedAsync(IServiceProvider services, InitialAdminSettings settings)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var roleName in Roles)
        {
            if (await roleManager.RoleExistsAsync(roleName))
                continue;

            var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName) { Id = Guid.NewGuid() });
            if (!roleResult.Succeeded)
                throw new InvalidOperationException($"Could not initialize the required '{roleName}' role.");
        }

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = provider.GetRequiredService<MediCareDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var admin = await userManager.FindByEmailAsync(settings.Email);
        if (admin is null)
        {
            if (await userManager.GetUsersInRoleAsync(MediCareRoles.Admin) is { Count: > 0 })
                throw new InvalidOperationException("An Admin already exists, but it does not match the configured bootstrap account.");

            admin = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = settings.Email,
                Email = settings.Email,
                FullName = settings.FullName,
                EmailConfirmed = true,
                IsActive = true,
                MustChangePassword = false
            };

            var createResult = await userManager.CreateAsync(admin, settings.Password);
            if (!createResult.Succeeded)
                throw new InvalidOperationException("Could not create the configured bootstrap Admin account.");

            var addResult = await userManager.AddToRoleAsync(admin, MediCareRoles.Admin);
            if (!addResult.Succeeded)
                throw new InvalidOperationException("Could not assign the Admin role to the configured bootstrap account.");
        }
        else if (!await userManager.IsInRoleAsync(admin, MediCareRoles.Admin))
        {
            // Never promote an existing non-Admin account that happens to use the bootstrap email.
            throw new InvalidOperationException("The configured bootstrap email belongs to an existing non-Admin account. Refusing to grant the Admin role.");
        }

        await transaction.CommitAsync();
        // Seeding creates Identity data only. Schema changes are applied explicitly by an operator.
    }
}
