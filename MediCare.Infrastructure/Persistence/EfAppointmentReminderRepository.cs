using MediCare.Application.Abstractions;
using MediCare.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

public sealed class EfAppointmentReminderRepository(MediCareDbContext dbContext) : IAppointmentReminderRepository
{
    public async Task<IReadOnlyList<ReminderCandidate>> GetConfirmedForDateAsync(DateOnly appointmentDate, CancellationToken cancellationToken) =>
        await dbContext.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.AppointmentDate == appointmentDate && appointment.Status == AppointmentStatus.Confirmed)
            .OrderBy(appointment => appointment.StartTime)
            .Select(appointment => new ReminderCandidate(
                appointment.Id,
                appointment.PatientId,
                appointment.AppointmentDate,
                appointment.StartTime,
                appointment.Doctor.FullName))
            .ToListAsync(cancellationToken);
}
