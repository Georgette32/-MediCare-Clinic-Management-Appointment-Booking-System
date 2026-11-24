using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Application.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

public sealed class EfAccountProvisioningRepository(MediCareDbContext dbContext) : IAccountProvisioningRepository
{
    public Task<bool> PatientPhoneExistsAsync(string canonicalPhone, CancellationToken cancellationToken) =>
        dbContext.Patients.AnyAsync(patient => patient.PhoneNumber == canonicalPhone, cancellationToken);

    public Task<bool> SpecializationExistsAsync(Guid specializationId, CancellationToken cancellationToken) =>
        dbContext.Specializations.AnyAsync(item => item.Id == specializationId, cancellationToken);

    public async Task<IReadOnlyList<SpecializationChoice>> GetSpecializationsAsync(CancellationToken cancellationToken) =>
        await dbContext.Specializations.AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => new SpecializationChoice(item.Id, item.Name))
            .ToArrayAsync(cancellationToken);

    public Task AddPatientAsync(Patient patient, CancellationToken cancellationToken) =>
        dbContext.Patients.AddAsync(patient, cancellationToken).AsTask();

    public Task AddDoctorAsync(Doctor doctor, CancellationToken cancellationToken) =>
        dbContext.Doctors.AddAsync(doctor, cancellationToken).AsTask();

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraint(exception))
        {
            throw new AccountProvisioningConflictException("An account with the provided details already exists.");
        }
    }

    private static bool IsUniqueConstraint(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is SqlException sql && sql.Number is 2601 or 2627)
                return true;
        return false;
    }
}
