using MediCare.Application.Interfaces.Persistence;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence.Repositories;

public sealed class MedicalRecordRepository : IMedicalRecordRepository
{
    private readonly ApplicationDbContext _dbContext;

    public MedicalRecordRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<MedicalRecord?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default)
    {
        IQueryable<MedicalRecord> query = _dbContext.Set<MedicalRecord>();
        if (!track)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<MedicalRecord>> GetByPatientAsync(Guid patientProfileId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<MedicalRecord>()
            .AsNoTracking()
            .Where(r => r.PatientProfileId == patientProfileId && !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(MedicalRecord record, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<MedicalRecord>().AddAsync(record, cancellationToken);

    public Task<bool> DoctorHasCompletedAppointmentWithPatientAsync(
        Guid doctorProfileId,
        Guid patientProfileId,
        Guid? appointmentId,
        CancellationToken cancellationToken = default)
    {
        var appointments = _dbContext.Appointments
            .AsNoTracking()
            .Where(a =>
                a.DoctorProfileId == doctorProfileId &&
                a.PatientProfileId == patientProfileId &&
                a.Status == AppointmentStatus.Completed);

        if (appointmentId.HasValue)
        {
            var id = appointmentId.Value;
            appointments = appointments.Where(a => a.Id == id);
        }

        return appointments.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PatientProfile>> GetPatientsWithCompletedAppointmentsAsync(
        Guid doctorProfileId,
        CancellationToken cancellationToken = default)
    {
        var patientIds = _dbContext.Appointments
            .AsNoTracking()
            .Where(a => a.DoctorProfileId == doctorProfileId && a.Status == AppointmentStatus.Completed)
            .Select(a => a.PatientProfileId)
            .Distinct();

        return await _dbContext.PatientProfiles
            .AsNoTracking()
            .Where(p => patientIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
    }
}
