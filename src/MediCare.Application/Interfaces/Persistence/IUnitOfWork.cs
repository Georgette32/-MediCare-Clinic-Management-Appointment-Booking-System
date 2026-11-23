using System.Data;
using MediCare.Application.Common;

namespace MediCare.Application.Interfaces.Persistence;

/// <summary>
/// Coordinates a single unit of work across repositories. Identity operations use
/// their own store and are compensated explicitly by the application services.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IUnitOfWorkTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken);

    Task<T> ExecuteSerializableWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken);

    Task<T> ExecuteWalkInRegistrationWithRetryAsync<T>(
        Guid doctorProfileId,
        DateOnly clinicDate,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken);

    Task<T> ExecuteDoctorQueueWithRetryAsync<T>(
        Guid doctorProfileId,
        DateOnly clinicDate,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken);
}

public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
