using MediCare.Domain.Entities;

namespace MediCare.Application.Abstractions;

public interface IMedicalAttachmentRepository
{
    /// <summary>The attachment with its uploader loaded, or null.</summary>
    Task<MedicalAttachment?> GetByIdAsync(Guid attachmentId, CancellationToken cancellationToken);
}
