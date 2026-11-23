using MediCare.Application.Interfaces.Persistence;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence.Repositories;

public sealed class WalkInAdmissionApprovalRepository : IWalkInAdmissionApprovalRepository
{
    private readonly ApplicationDbContext _dbContext;

    public WalkInAdmissionApprovalRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> HasPendingForQueueEntryAsync(
        Guid queueEntryId,
        CancellationToken cancellationToken) =>
        _dbContext.WalkInAdmissionApprovals.AnyAsync(
            approval => approval.QueueEntryId == queueEntryId &&
                        approval.Decision == WalkInAdmissionDecision.Pending,
            cancellationToken);

    public async Task AddAsync(
        WalkInAdmissionApproval approval,
        CancellationToken cancellationToken) =>
        await _dbContext.WalkInAdmissionApprovals.AddAsync(approval, cancellationToken);

    public Task<WalkInAdmissionApproval?> GetByIdAsync(
        Guid approvalId,
        CancellationToken cancellationToken) =>
        _dbContext.WalkInAdmissionApprovals
            .Include(approval => approval.QueueEntry)
                .ThenInclude(entry => entry.PatientProfile)
            .Include(approval => approval.QueueEntry)
                .ThenInclude(entry => entry.Appointment)
            .SingleOrDefaultAsync(approval => approval.Id == approvalId, cancellationToken);

    public async Task<IReadOnlyList<WalkInAdmissionApproval>> GetPendingForDoctorAsync(
        Guid doctorProfileId,
        CancellationToken cancellationToken) =>
        await _dbContext.WalkInAdmissionApprovals
            .Include(approval => approval.QueueEntry)
                .ThenInclude(entry => entry.PatientProfile)
            .Where(approval => approval.DoctorProfileId == doctorProfileId &&
                               approval.Decision == WalkInAdmissionDecision.Pending)
            .OrderBy(approval => approval.RequestedAtUtc)
            .ToListAsync(cancellationToken);
}
