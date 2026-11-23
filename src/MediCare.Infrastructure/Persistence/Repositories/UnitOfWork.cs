using System.Data;
using MediCare.Application.Common.Exceptions;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Domain.Entities;
using MediCare.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediCare.Infrastructure.Persistence.Repositories;

public sealed class UnitOfWork : IUnitOfWork
{
    private const int MaxSerializableAttempts = 5;

    private readonly ApplicationDbContext _dbContext;

    public UnitOfWork(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(
                "A concurrency conflict occurred while saving changes.",
                ex);
        }
        catch (DbUpdateException ex) when (IsDuplicateInvoice(ex))
        {
            throw new DuplicateInvoiceException(
                "An invoice already exists for this appointment.",
                ex);
        }
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync(
            isolationLevel, cancellationToken);
        return new EfUnitOfWorkTransaction(transaction);
    }

    public Task<T> ExecuteSerializableWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) =>
        ExecuteWithRetryAsync(operation, lockResource: null, cancellationToken);

    public Task<T> ExecuteWalkInRegistrationWithRetryAsync<T>(
        Guid doctorProfileId,
        DateOnly clinicDate,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) =>
        ExecuteDoctorQueueWithRetryAsync(doctorProfileId, clinicDate, operation, cancellationToken);

    public Task<T> ExecuteDoctorQueueWithRetryAsync<T>(
        Guid doctorProfileId,
        DateOnly clinicDate,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) =>
        ExecuteWithRetryAsync(
            operation,
            $"MediCareDoctorQueue:{doctorProfileId:N}:{clinicDate:yyyyMMdd}",
            cancellationToken);

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string? lockResource,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);

                if (lockResource is not null)
                {
                    await using var command = _dbContext.Database.GetDbConnection().CreateCommand();
                    command.Transaction = transaction.GetDbTransaction();
                    command.CommandText =
                        "DECLARE @LockResult int; " +
                        "EXEC @LockResult = sys.sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; " +
                        "SELECT @LockResult;";

                    var resourceParameter = command.CreateParameter();
                    resourceParameter.ParameterName = "@Resource";
                    resourceParameter.Value = lockResource;
                    command.Parameters.Add(resourceParameter);

                    var lockResult = Convert.ToInt32(
                        await command.ExecuteScalarAsync(cancellationToken));
                    if (lockResult < 0)
                    {
                        throw new InvalidOperationException(
                            $"Could not acquire the doctor queue lock (SQL lock result {lockResult}).");
                    }
                }

                // Reload after the lock so validation uses current database state.
                _dbContext.ChangeTracker.Clear();

                var result = await operation(cancellationToken);
                await SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception exception)
                when (attempt < MaxSerializableAttempts && IsRetryableSqlException(exception))
            {
                _dbContext.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
        }
    }

    private static bool IsRetryableSqlException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException && sqlException.Number is 1205 or 2601 or 2627)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDuplicateInvoice(DbUpdateException exception)
    {
        var invoiceEntry = exception.Entries.Any(entry => entry.Entity is Invoice);
        if (!invoiceEntry)
        {
            return false;
        }

        var sqlException = exception.InnerException as SqlException;
        return sqlException is not null &&
               (sqlException.Number == 2601 || sqlException.Number == 2627);
    }

    private sealed class EfUnitOfWorkTransaction(IDbContextTransaction transaction)
        : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) =>
            transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
