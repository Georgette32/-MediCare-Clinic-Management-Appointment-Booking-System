using MediCare.Application.Abstractions;
using MediCare.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

public sealed class EfPreviousDayAppointmentRepository(MediCareDbContext dbContext) : IPreviousDayAppointmentRepository
{
    public async Task<IReadOnlyList<Guid>> GetConfirmedBeforeDateAsync(DateOnly date, CancellationToken cancellationToken) =>
        await dbContext.Appointments
            .Where(item => item.Status == AppointmentStatus.Confirmed && item.AppointmentDate < date)
            .OrderBy(item => item.AppointmentDate)
            .ThenBy(item => item.StartTime)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<QueueEntryStatus>> GetAttemptStatusesAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        await dbContext.QueueEntries
            .Where(item => item.AppointmentId == appointmentId)
            .Select(item => item.Status)
            .ToListAsync(cancellationToken);
}
