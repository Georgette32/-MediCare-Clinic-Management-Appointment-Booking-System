using MediCare.Domain.Entities;

namespace MediCare.Application.Abstractions;

public interface IWalkInAdmissionApprovalRepository
{
    Task<bool> HasPendingForQueueEntryAsync(Guid queueEntryId, CancellationToken cancellationToken);
    Task AddAsync(WalkInAdmissionApproval approval, CancellationToken cancellationToken);
    Task<WalkInAdmissionApproval?> GetByIdAsync(Guid approvalId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WalkInAdmissionApproval>> GetPendingForDoctorAsync(Guid doctorId, CancellationToken cancellationToken);
}
