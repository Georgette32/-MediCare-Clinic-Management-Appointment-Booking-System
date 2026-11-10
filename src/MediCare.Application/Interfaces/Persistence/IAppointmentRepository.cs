using MediCare.Domain.Enums;

namespace MediCare.Application.Interfaces.Persistence;

public interface IAppointmentRepository
{
    Task<int> CountAsync(AppointmentStatus? status = null, CancellationToken cancellationToken = default);

    Task<int> CountUpcomingAsync(CancellationToken cancellationToken = default);
}
