using MediCare.Application.Interfaces.Persistence;
using MediCare.Domain.Entities;
using MediCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence.Repositories;

public sealed class PrescriptionRepository : IPrescriptionRepository
{
    private readonly ApplicationDbContext _dbContext;

    public PrescriptionRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Prescription?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default)
    {
        IQueryable<Prescription> query = _dbContext.Set<Prescription>()
            .Include(p => p.Items);

        if (!track)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Prescription>> GetByPatientAsync(Guid patientProfileId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Prescription>()
            .AsNoTracking()
            .Include(p => p.Items)
            .Where(p => p.PatientProfileId == patientProfileId && !p.IsDeleted)
            .OrderByDescending(p => p.IssuedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Prescription prescription, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Prescription>().AddAsync(prescription, cancellationToken);
}
