using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

public sealed class EfQueueBoardRepository(MediCareDbContext dbContext) : IQueueBoardRepository
{
    public async Task<IReadOnlyList<Appointment>> GetDoctorAppointmentsForDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        await dbContext.Appointments
            .AsNoTracking()
            .Include(item => item.Patient)
            .Where(item => item.DoctorId == doctorId && item.AppointmentDate == date)
            .OrderBy(item => item.StartTime)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<QueueEntry>> GetDoctorQueueEntriesForDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        await dbContext.QueueEntries
            .AsNoTracking()
            .Include(item => item.Patient)
            .Include(item => item.Appointment)
            .Where(item => item.DoctorId == doctorId && item.QueueDate == date)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Doctor>> GetActiveDoctorsAsync(CancellationToken cancellationToken) =>
        await dbContext.Doctors
            .AsNoTracking()
            .Where(doctor => doctor.IsActive)
            .OrderBy(doctor => doctor.FullName)
            .ToListAsync(cancellationToken);

    public Task<Guid?> FindPatientIdByPhoneAsync(string canonicalPhone, CancellationToken cancellationToken) =>
        dbContext.Patients
            .AsNoTracking()
            .Where(patient => patient.PhoneNumber == canonicalPhone)
            .Select(patient => (Guid?)patient.Id)
            .SingleOrDefaultAsync(cancellationToken);
}
