using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

public sealed class EfAppointmentRepository(MediCareDbContext dbContext) : IAppointmentRepository
{
    public Task<bool> PatientExistsAsync(Guid patientId, CancellationToken cancellationToken) =>
        dbContext.Patients.AnyAsync(patient => patient.Id == patientId, cancellationToken);

    public Task<bool> HasConfirmedAppointmentForPatientAndDoctorAndDateAsync(Guid patientId, Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        dbContext.Appointments.AnyAsync(appointment => appointment.PatientId == patientId &&
            appointment.DoctorId == doctorId && appointment.AppointmentDate == date &&
            appointment.Status == AppointmentStatus.Confirmed, cancellationToken);

    public Task<Doctor?> GetDoctorAsync(Guid doctorId, CancellationToken cancellationToken) =>
        dbContext.Doctors.SingleOrDefaultAsync(doctor => doctor.Id == doctorId && doctor.IsActive, cancellationToken);

    public Task<Doctor?> GetDoctorByApplicationUserIdAsync(Guid applicationUserId, CancellationToken cancellationToken) =>
        dbContext.Doctors.SingleOrDefaultAsync(doctor => doctor.ApplicationUserId == applicationUserId && doctor.IsActive, cancellationToken);

    public async Task<IReadOnlyList<DoctorWorkingHour>> GetWorkingHoursAsync(Guid doctorId, DayOfWeek dayOfWeek, CancellationToken cancellationToken) =>
        await dbContext.DoctorWorkingHours
            .Where(schedule => schedule.DoctorId == doctorId && schedule.DayOfWeek == dayOfWeek && schedule.IsActive)
            .OrderBy(schedule => schedule.StartTime)
            .ToListAsync(cancellationToken);

    public Task<bool> IsHolidayAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        dbContext.DoctorHolidays.AnyAsync(holiday => holiday.DoctorId == doctorId && holiday.StartDate <= date && holiday.EndDate >= date, cancellationToken);

    public Task<bool> HasBreakOverlapAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, CancellationToken cancellationToken) =>
        dbContext.DoctorBreaks.AnyAsync(item => item.DoctorId == doctorId && item.Date == date && item.StartTime < end && item.EndTime > start, cancellationToken);

    public Task<bool> HasAppointmentConflictAsync(Guid doctorId, DateOnly date, TimeOnly start, TimeOnly end, Guid? exceptAppointmentId, CancellationToken cancellationToken) =>
        dbContext.Appointments.AnyAsync(item =>
            item.DoctorId == doctorId &&
            item.AppointmentDate == date &&
            item.Id != exceptAppointmentId &&
            item.StartTime < end &&
            item.EndTime > start &&
            (item.Status == AppointmentStatus.Pending || item.Status == AppointmentStatus.Confirmed), cancellationToken);

    public Task AddAsync(Appointment appointment, CancellationToken cancellationToken) =>
        dbContext.Appointments.AddAsync(appointment, cancellationToken).AsTask();

    public Task<Appointment?> GetByIdAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        dbContext.Appointments.SingleOrDefaultAsync(item => item.Id == appointmentId, cancellationToken);

    public Task<bool> HasLiveRescheduledSuccessorAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        dbContext.Appointments.AnyAsync(item =>
            item.RescheduledFromAppointmentId == appointmentId &&
            (item.Status == AppointmentStatus.Pending || item.Status == AppointmentStatus.Confirmed), cancellationToken);

    public async Task<IReadOnlyList<Appointment>> GetPendingAsync(CancellationToken cancellationToken) =>
        await dbContext.Appointments
            .Include(item => item.Patient)
            .Include(item => item.Doctor)
            .Where(item => item.Status == AppointmentStatus.Pending)
            .OrderBy(item => item.AppointmentDate)
            .ThenBy(item => item.StartTime)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Appointment>> GetConfirmedForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        await dbContext.Appointments
            .Include(item => item.Patient)
            .Include(item => item.Doctor)
            .Where(item => item.DoctorId == doctorId && item.AppointmentDate == date && item.Status == AppointmentStatus.Confirmed)
            .OrderBy(item => item.StartTime)
            .ToListAsync(cancellationToken);
}
