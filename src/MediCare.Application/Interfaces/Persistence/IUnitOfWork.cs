using MediCare.Application.Common;

namespace MediCare.Application.Interfaces.Persistence;

/// <summary>
/// Coordinates a single unit of work across repositories. Identity operations use
/// their own store and are compensated explicitly by the application services.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
