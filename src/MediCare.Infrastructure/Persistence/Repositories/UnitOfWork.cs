
using MediCare.Application.Common.Exceptions;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Domain.Entities;
using MediCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediCare.Infrastructure.Persistence.Repositories;

public sealed class UnitOfWork : IUnitOfWork
{
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

    private static bool IsDuplicateInvoice(DbUpdateException exception)
    {
        var invoiceEntry = exception.Entries
            .Any(entry => entry.Entity is Invoice);

        if (!invoiceEntry)
            return false;

        var sqlException = exception.InnerException as SqlException;

        return sqlException is not null &&
               (sqlException.Number == 2601 ||
                sqlException.Number == 2627);
    }
}
