using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

public sealed class EfWalkInAdmissionApprovalRepository(MediCareDbContext dbContext) : IWalkInAdmissionApprovalRepository
{
    public Task<bool> HasPendingForQueueEntryAsync(Guid queueEntryId, CancellationToken cancellationToken) =>
        dbContext.WalkInAdmissionApprovals.AnyAsync(approval =>
            approval.QueueEntryId == queueEntryId && approval.Decision == WalkInAdmissionDecision.Pending,
            cancellationToken);

    public Task AddAsync(WalkInAdmissionApproval approval, CancellationToken cancellationToken) =>
        dbContext.WalkInAdmissionApprovals.AddAsync(approval, cancellationToken).AsTask();

    public Task<WalkInAdmissionApproval?> GetByIdAsync(Guid approvalId, CancellationToken cancellationToken) =>
        dbContext.WalkInAdmissionApprovals
            .Include(approval => approval.QueueEntry)
                .ThenInclude(entry => entry.Patient)
            .Include(approval => approval.QueueEntry)
                .ThenInclude(entry => entry.Appointment)
            .SingleOrDefaultAsync(approval => approval.Id == approvalId, cancellationToken);

    public async Task<IReadOnlyList<WalkInAdmissionApproval>> GetPendingForDoctorAsync(Guid doctorId, CancellationToken cancellationToken) =>
        await dbContext.WalkInAdmissionApprovals
            .Include(approval => approval.QueueEntry)
                .ThenInclude(entry => entry.Patient)
            .Where(approval => approval.DoctorId == doctorId && approval.Decision == WalkInAdmissionDecision.Pending)
            .OrderBy(approval => approval.RequestedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WalkInAdmissionApproval>> GetForQueueEntriesAsync(IReadOnlyCollection<Guid> queueEntryIds, CancellationToken cancellationToken) =>
        await dbContext.WalkInAdmissionApprovals
            .Where(approval => queueEntryIds.Contains(approval.QueueEntryId))
            .OrderBy(approval => approval.RequestedAtUtc)
            .ToListAsync(cancellationToken);
}
