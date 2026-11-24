using System.Data;
using MediCare.Application.Abstractions;
using MediCare.Application.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediCare.Infrastructure.Persistence;

public sealed partial class EfUnitOfWork(MediCareDbContext dbContext) : IUnitOfWork
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
            catch (Exception exception) when (TryGetUniqueIndexName(exception, out var indexName))
            {
                // The last attempt still hit a unique index: report the business conflict, not a 500.
                dbContext.ChangeTracker.Clear();
                throw new UniqueConstraintConflictException(indexName!, FriendlyUniqueConflictMessage(indexName!), exception);
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

    private static bool TryGetUniqueIndexName(Exception exception, out string? indexName)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 2601 or 2627 } sqlException)
            {
                var match = UniqueIndexPattern().Match(sqlException.Message);
                indexName = match.Success ? match.Groups[1].Value : "unknown";
                return true;
            }
        }

        indexName = null;
        return false;
    }

    /// <summary>User-facing text for each unique index the application relies on. Unknown indexes get a generic message.</summary>
    private static string FriendlyUniqueConflictMessage(string indexName) => indexName switch
    {
        "IX_Appointments_DoctorId_AppointmentDate_StartTime" => "The requested appointment time is already reserved.",
        "IX_QueueEntries_DoctorId" => "Another consultation is already in progress for this doctor. Complete it first.",
        "IX_QueueEntries_AppointmentId" => "The patient has already checked in for this appointment.",
        "IX_QueueEntries_DoctorId_QueueDate_QueueSequence" => "The walk-in queue changed while registering. Please try again.",
        "IX_WalkInAdmissionApprovals_QueueEntryId" => "A pending admission request already exists for this queue entry.",
        "IX_Appointments_RescheduledFromAppointmentId" => "This appointment already has a live replacement request.",
        _ => "The change conflicted with another update. Please try again."
    };

    [System.Text.RegularExpressions.GeneratedRegex("unique index '([^']+)'")]
    private static partial System.Text.RegularExpressions.Regex UniqueIndexPattern();

    private sealed class EfUnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
