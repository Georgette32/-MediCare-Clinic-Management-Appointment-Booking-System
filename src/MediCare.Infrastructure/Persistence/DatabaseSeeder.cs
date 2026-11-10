using MediCare.Application.Common.Security;
using MediCare.Domain.Entities;
using MediCare.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediCare.Infrastructure.Persistence;

public sealed class SeedOptions
{
    public string AdminEmail { get; set; } = "admin@medicare.local";

    public string AdminPassword { get; set; } = "Admin@12345";

    public string AdminFullName { get; set; } = "MediCare Administrator";

    public bool SeedSampleSpecializations { get; set; } = true;
}

/// <summary>
/// Applies migrations and creates the baseline data required to operate the system:
/// roles, a bootstrap administrator and the initial specialization list.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        SeedOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;

        var dbContext = provider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);

        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() });
                if (!roleResult.Succeeded)
                {
                    logger.LogError("Could not create role {Role}: {Errors}", role,
                        string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                }
            }
        }

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        if (!string.IsNullOrWhiteSpace(options.AdminEmail) && !string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            var admin = await userManager.FindByEmailAsync(options.AdminEmail);
            if (admin is null)
            {
                admin = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = options.AdminEmail,
                    Email = options.AdminEmail,
                    FullName = options.AdminFullName,
                    EmailConfirmed = true,
                    IsActive = true
                };

                var created = await userManager.CreateAsync(admin, options.AdminPassword);
                if (created.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, Roles.Admin);
                    logger.LogInformation("Bootstrap administrator {Email} created.", options.AdminEmail);
                }
                else
                {
                    logger.LogError("Could not create the bootstrap administrator: {Errors}",
                        string.Join(", ", created.Errors.Select(e => e.Description)));
                }
            }
        }

        if (options.SeedSampleSpecializations && !await dbContext.Specializations.AnyAsync(cancellationToken))
        {
            dbContext.Specializations.AddRange(
                new Specialization("General Medicine", "Primary care and general consultations."),
                new Specialization("Cardiology", "Heart and cardiovascular care."),
                new Specialization("Dermatology", "Skin, hair and nail conditions."),
                new Specialization("Pediatrics", "Medical care for children and adolescents."),
                new Specialization("Orthopedics", "Bones, joints and musculoskeletal care."),
                new Specialization("Neurology", "Brain, spinal cord and nervous system care."));

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Sample specializations were seeded.");
        }
    }
}
