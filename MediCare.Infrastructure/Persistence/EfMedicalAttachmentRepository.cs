using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

public sealed class EfMedicalAttachmentRepository(MediCareDbContext dbContext) : IMedicalAttachmentRepository
{
    public Task<MedicalAttachment?> GetByIdAsync(Guid attachmentId, CancellationToken cancellationToken) =>
        dbContext.MedicalAttachments
            .Include(attachment => attachment.UploadedByDoctor)
            .SingleOrDefaultAsync(attachment => attachment.Id == attachmentId, cancellationToken);
}
