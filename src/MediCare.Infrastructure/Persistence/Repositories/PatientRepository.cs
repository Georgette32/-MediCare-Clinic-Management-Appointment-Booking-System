using MediCare.Application.Interfaces.Persistence;
using MediCare.Domain.Entities;
using MediCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence.Repositories;

public sealed class PatientRepository : IPatientRepository
{
    private readonly ApplicationDbContext _dbContext;

    public PatientRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PatientProfile?> GetByUserIdAsync(Guid userId, bool track = true, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.PatientProfiles.AsQueryable();
        if (!track)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        _dbContext.PatientProfiles.CountAsync(cancellationToken);

    public async Task AddAsync(PatientProfile profile, CancellationToken cancellationToken = default) =>
        await _dbContext.PatientProfiles.AddAsync(profile, cancellationToken);
}
