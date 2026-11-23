using System.Data;
using MediCare.Application.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediCare.Infrastructure.Persistence;

public sealed class EfUnitOfWork(MediCareDbContext dbContext) : IUnitOfWork
{
    private const int MaxSerializableAttempts = 5;

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
        return new EfUnitOfWorkTransaction(transaction);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public async Task<T> ExecuteSerializableWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        => await ExecuteWithRetryAsync(operation, lockResource: null, cancellationToken);

    public Task<T> ExecuteWalkInRegistrationWithRetryAsync<T>(Guid doctorId, DateOnly clinicDate, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        => ExecuteDoctorQueueWithRetryAsync(doctorId, clinicDate, operation, cancellationToken);

    public Task<T> ExecuteDoctorQueueWithRetryAsync<T>(Guid doctorId, DateOnly clinicDate, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        => ExecuteWithRetryAsync(operation, $"MediCareDoctorQueue:{doctorId:N}:{clinicDate:yyyyMMdd}", cancellationToken);

    private async Task<T> ExecuteWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, string? lockResource, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                if (lockResource is not null)
                {
                    await using var command = dbContext.Database.GetDbConnection().CreateCommand();
                    command.Transaction = transaction.GetDbTransaction();
                    command.CommandText = "DECLARE @LockResult int; EXEC @LockResult = sys.sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; SELECT @LockResult;";
                    var resourceParameter = command.CreateParameter();
                    resourceParameter.ParameterName = "@Resource";
                    resourceParameter.Value = lockResource;
                    command.Parameters.Add(resourceParameter);
                    var lockResult = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
                    if (lockResult < 0)
                        throw new InvalidOperationException($"Could not acquire the walk-in sequence lock (SQL lock result {lockResult}).");
                }

                // Reload all entities after taking the queue lock so validations run against current database state.
                dbContext.ChangeTracker.Clear();

                var result = await operation(cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception exception) when (attempt < MaxSerializableAttempts && IsRetryableSqlException(exception))
            {
                dbContext.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
        }
    }

    private static bool IsRetryableSqlException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException && sqlException.Number is 1205 or 2601 or 2627)
                return true;
        }

        return false;
    }

    private sealed class EfUnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
