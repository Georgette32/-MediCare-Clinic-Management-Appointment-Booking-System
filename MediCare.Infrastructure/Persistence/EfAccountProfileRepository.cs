using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

/// <summary>Tracked profile rows so the profile service can update display names in the same unit of work.</summary>
public sealed class EfAccountProfileRepository(MediCareDbContext dbContext) : IAccountProfileRepository
{
    public Task<Patient?> GetPatientByApplicationUserIdAsync(Guid applicationUserId, CancellationToken cancellationToken) =>
        dbContext.Patients.SingleOrDefaultAsync(patient => patient.ApplicationUserId == applicationUserId, cancellationToken);

    public Task<Doctor?> GetDoctorByApplicationUserIdAsync(Guid applicationUserId, CancellationToken cancellationToken) =>
        dbContext.Doctors.SingleOrDefaultAsync(doctor => doctor.ApplicationUserId == applicationUserId, cancellationToken);
}
