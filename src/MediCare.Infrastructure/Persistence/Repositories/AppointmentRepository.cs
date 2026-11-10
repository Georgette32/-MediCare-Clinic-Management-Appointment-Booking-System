using MediCare.Application.Interfaces.Persistence;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence.Repositories;

public sealed class AppointmentRepository : IAppointmentRepository
{
    private readonly ApplicationDbContext _dbContext;

    public AppointmentRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> CountAsync(AppointmentStatus? status = null, CancellationToken cancellationToken = default) =>
        status.HasValue
            ? _dbContext.Appointments.CountAsync(a => a.Status == status.Value, cancellationToken)
            : _dbContext.Appointments.CountAsync(cancellationToken);

    public Task<int> CountUpcomingAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return _dbContext.Appointments.CountAsync(
            a => a.ScheduledAtUtc >= now &&
                 a.Status != AppointmentStatus.Cancelled &&
                 a.Status != AppointmentStatus.Completed,
            cancellationToken);
    }
}
