using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

public sealed class EfSpecializationAdministrationRepository(MediCareDbContext dbContext) : ISpecializationAdministrationRepository
{
    public async Task<IReadOnlyList<SpecializationAdminRow>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Specializations.AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => new SpecializationAdminRow(
                item.Id,
                item.Name,
                item.Description,
                dbContext.Doctors.Count(doctor => doctor.SpecializationId == item.Id)))
            .ToListAsync(cancellationToken);

    public Task<Specialization?> GetTrackedAsync(Guid specializationId, CancellationToken cancellationToken) =>
        dbContext.Specializations.SingleOrDefaultAsync(item => item.Id == specializationId, cancellationToken);

    public Task<bool> NameExistsAsync(string name, Guid? exceptSpecializationId, CancellationToken cancellationToken) =>
        dbContext.Specializations.AnyAsync(item => item.Name == name && (exceptSpecializationId == null || item.Id != exceptSpecializationId), cancellationToken);

    public Task<int> CountDoctorsAsync(Guid specializationId, CancellationToken cancellationToken) =>
        dbContext.Doctors.CountAsync(doctor => doctor.SpecializationId == specializationId, cancellationToken);

    public Task AddAsync(Specialization specialization, CancellationToken cancellationToken) =>
        dbContext.Specializations.AddAsync(specialization, cancellationToken).AsTask();

    public void Remove(Specialization specialization) => dbContext.Specializations.Remove(specialization);
}

public sealed class EfStaffAccountRepository(MediCareDbContext dbContext) : IStaffAccountRepository
{
    public async Task<IReadOnlyList<StaffAccountRow>> ListStaffAsync(CancellationToken cancellationToken)
    {
        var accounts = await (from user in dbContext.Users
                              join userRole in dbContext.UserRoles on user.Id equals userRole.UserId
                              join role in dbContext.Roles on userRole.RoleId equals role.Id
                              where role.Name == MediCareRoles.Doctor || role.Name == MediCareRoles.Receptionist
                              select new { user.Id, user.FullName, user.UserName, user.IsActive, RoleName = role.Name })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var doctors = await dbContext.Doctors.AsNoTracking()
            .Include(doctor => doctor.Specialization)
            .ToDictionaryAsync(doctor => doctor.ApplicationUserId, cancellationToken);

        return accounts
            .OrderBy(account => account.RoleName)
            .ThenBy(account => account.FullName)
            .Select(account =>
            {
                doctors.TryGetValue(account.Id, out var doctor);
                return new StaffAccountRow(
                    account.Id,
                    account.FullName,
                    account.UserName ?? string.Empty,
                    account.RoleName ?? string.Empty,
                    account.IsActive,
                    doctor?.Id,
                    doctor?.Specialization.Name);
            })
            .ToArray();
    }

    public Task<string?> GetRoleNameAsync(Guid userId, CancellationToken cancellationToken) =>
        (from userRole in dbContext.UserRoles
         join role in dbContext.Roles on userRole.RoleId equals role.Id
         where userRole.UserId == userId
         select role.Name)
        .AsNoTracking()
        .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> SetAccountActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null)
            return false;

        user.IsActive = isActive;
        var doctor = await dbContext.Doctors.SingleOrDefaultAsync(item => item.ApplicationUserId == userId, cancellationToken);
        if (doctor is not null)
        {
            if (isActive)
                doctor.Activate();
            else
                doctor.Deactivate();
        }

        return true;
    }
}

public sealed class EfClinicSettingsRepository(MediCareDbContext dbContext) : IClinicSettingsRepository
{
    public Task<ClinicSettings?> GetAsync(CancellationToken cancellationToken) =>
        dbContext.ClinicSettings.SingleOrDefaultAsync(item => item.Id == ClinicSettings.SingletonId, cancellationToken);

    public Task AddAsync(ClinicSettings settings, CancellationToken cancellationToken) =>
        dbContext.ClinicSettings.AddAsync(settings, cancellationToken).AsTask();
}
