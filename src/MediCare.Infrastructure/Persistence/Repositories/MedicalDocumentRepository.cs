using MediCare.Application.Interfaces.Persistence;
using MediCare.Domain.Entities;
using MediCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence.Repositories;

public sealed class MedicalDocumentRepository : IMedicalDocumentRepository
{
    private readonly ApplicationDbContext _dbContext;

    public MedicalDocumentRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<MedicalDocument?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default)
    {
        IQueryable<MedicalDocument> query = _dbContext.Set<MedicalDocument>();
        if (!track)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<MedicalDocument>> GetByPatientAsync(Guid patientProfileId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<MedicalDocument>()
            .AsNoTracking()
            .Where(d => d.PatientProfileId == patientProfileId && !d.IsDeleted)
            .OrderByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(MedicalDocument document, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<MedicalDocument>().AddAsync(document, cancellationToken);
}
