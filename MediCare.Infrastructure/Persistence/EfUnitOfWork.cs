using System.Data;
using MediCare.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediCare.Infrastructure.Persistence;

public sealed class EfUnitOfWork(MediCareDbContext dbContext) : IUnitOfWork
{
    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
        return new EfUnitOfWorkTransaction(transaction);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private sealed class EfUnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
