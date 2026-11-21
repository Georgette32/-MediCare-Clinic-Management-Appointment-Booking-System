using System.Data;

namespace MediCare.Application.Abstractions;

public interface IUnitOfWork
{
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
