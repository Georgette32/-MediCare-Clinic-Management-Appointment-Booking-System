using MediCare.Application.Interfaces.Persistence;
using MediCare.Domain.Entities;
using MediCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence.Repositories;

public sealed class SpecializationRepository : ISpecializationRepository
{
    private readonly ApplicationDbContext _dbContext;

    public SpecializationRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Specialization>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Specializations
            .Include(s => s.Doctors)
            .AsNoTracking()
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        return await query.OrderBy(s => s.Name).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Specialization>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Specializations
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

    public async Task<Specialization?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Specializations
            .Include(s => s.Doctors)
            .AsQueryable();

        if (!track)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        return _dbContext.Specializations.AnyAsync(
            s => s.Name == trimmed && (!excludeId.HasValue || s.Id != excludeId.Value),
            cancellationToken);
    }

    public Task<int> CountDoctorsAsync(Guid specializationId, CancellationToken cancellationToken = default) =>
        _dbContext.DoctorProfiles.CountAsync(d => d.SpecializationId == specializationId, cancellationToken);

    public async Task AddAsync(Specialization specialization, CancellationToken cancellationToken = default) =>
        await _dbContext.Specializations.AddAsync(specialization, cancellationToken);

    public void Remove(Specialization specialization) => _dbContext.Specializations.Remove(specialization);
}
