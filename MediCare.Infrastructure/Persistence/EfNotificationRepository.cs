using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

public sealed class EfNotificationRepository(MediCareDbContext dbContext) : INotificationRepository
{
    public Task<Guid?> GetPatientUserIdAsync(Guid patientId, CancellationToken cancellationToken) =>
        dbContext.Patients
            .Where(patient => patient.Id == patientId)
            .Select(patient => (Guid?)patient.ApplicationUserId)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<Guid?> GetDoctorUserIdAsync(Guid doctorId, CancellationToken cancellationToken) =>
        dbContext.Doctors
            .Where(doctor => doctor.Id == doctorId)
            .Select(doctor => (Guid?)doctor.ApplicationUserId)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsForDedupKeyAsync(Guid recipientUserId, string dedupKey, CancellationToken cancellationToken) =>
        dbContext.Notifications.AnyAsync(item => item.RecipientUserId == recipientUserId && item.DedupKey == dedupKey, cancellationToken);

    public Task AddAsync(Notification notification, CancellationToken cancellationToken) =>
        dbContext.Notifications.AddAsync(notification, cancellationToken).AsTask();

    public void Detach(Notification notification) =>
        dbContext.Entry(notification).State = Microsoft.EntityFrameworkCore.EntityState.Detached;

    public async Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetPageForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.Notifications.AsNoTracking().Where(item => item.RecipientUserId == userId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Notifications.CountAsync(item => item.RecipientUserId == userId && item.ReadAtUtc == null, cancellationToken);

    public Task<Notification?> GetForRecipientAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.Notifications.SingleOrDefaultAsync(item => item.Id == notificationId && item.RecipientUserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Notification>> GetUnreadForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Notifications
            .Where(item => item.RecipientUserId == userId && item.ReadAtUtc == null)
            .ToListAsync(cancellationToken);
}
