using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

public sealed class EfDoctorDisruptionRepository(MediCareDbContext dbContext) : IDoctorDisruptionRepository
{
    public Task<DoctorDisruption?> GetByIdAsync(Guid disruptionId, CancellationToken cancellationToken) =>
        dbContext.DoctorDisruptions
            .Include(item => item.Doctor)
            .SingleOrDefaultAsync(item => item.Id == disruptionId, cancellationToken);

    public Task<bool> ExistsForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        dbContext.DoctorDisruptions.AnyAsync(item => item.DoctorId == doctorId && item.Date == date, cancellationToken);

    public Task<DoctorDisruption?> GetForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        dbContext.DoctorDisruptions
            .SingleOrDefaultAsync(item => item.DoctorId == doctorId && item.Date == date, cancellationToken);

    public async Task<IReadOnlyList<DoctorDisruption>> GetRecentAsync(int take, CancellationToken cancellationToken) =>
        await dbContext.DoctorDisruptions
            .Include(item => item.Doctor)
            .OrderByDescending(item => item.Date)
            .ThenByDescending(item => item.RecordedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task AddDisruptionAsync(DoctorDisruption disruption, CancellationToken cancellationToken) =>
        dbContext.DoctorDisruptions.AddAsync(disruption, cancellationToken).AsTask();

    public Task AddBreaksAsync(IReadOnlyList<DoctorBreak> breaks, CancellationToken cancellationToken)
    {
        dbContext.DoctorBreaks.AddRange(breaks);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<DisruptionWorklistItem>> GetItemsForDisruptionAsync(Guid disruptionId, CancellationToken cancellationToken) =>
        await dbContext.DisruptionWorklistItems
            .Include(item => item.Patient)
            .Include(item => item.Appointment)
            .Include(item => item.QueueEntry)
            .Where(item => item.DisruptionId == disruptionId)
            .ToListAsync(cancellationToken);

    public Task<DisruptionWorklistItem?> GetItemByIdAsync(Guid itemId, CancellationToken cancellationToken) =>
        dbContext.DisruptionWorklistItems
            .Include(item => item.Patient)
            .Include(item => item.Appointment)
            .Include(item => item.QueueEntry)
            .SingleOrDefaultAsync(item => item.Id == itemId, cancellationToken);

    public Task AddItemsAsync(IReadOnlyList<DisruptionWorklistItem> items, CancellationToken cancellationToken)
    {
        dbContext.DisruptionWorklistItems.AddRange(items);
        return Task.CompletedTask;
    }

    public Task AddContactAttemptAsync(DisruptionContactAttempt attempt, CancellationToken cancellationToken)
    {
        dbContext.DisruptionContactAttempts.Add(attempt);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<DisruptionContactAttempt>> GetAttemptsForDisruptionAsync(Guid disruptionId, CancellationToken cancellationToken)
    {
        var itemIds = dbContext.DisruptionWorklistItems
            .Where(item => item.DisruptionId == disruptionId)
            .Select(item => item.Id);

        return await dbContext.DisruptionContactAttempts
            .Where(attempt => itemIds.Contains(attempt.WorklistItemId))
            .OrderBy(attempt => attempt.ContactedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DisruptionWorklistItem>> GetOpenItemsUpToDateAsync(DateOnly date, CancellationToken cancellationToken) =>
        await dbContext.DisruptionWorklistItems
            .Include(item => item.Patient)
            .Include(item => item.Appointment)
            .Include(item => item.QueueEntry)
            .Where(item => item.ResolvedAtUtc == null && item.Disruption.Date <= date)
            .ToListAsync(cancellationToken);

    public Task<DisruptionWorklistItem?> GetOpenItemForAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        dbContext.DisruptionWorklistItems
            .Include(item => item.Appointment)
            .Include(item => item.Disruption)
                .ThenInclude(disruption => disruption.Doctor)
            .Where(item => item.AppointmentId == appointmentId && item.ResolvedAtUtc == null)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<DisruptionWorklistItem>> GetOpenItemsForDoctorAndDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken) =>
        await dbContext.DisruptionWorklistItems
            .Include(item => item.Disruption)
            .Where(item => item.ResolvedAtUtc == null &&
                           item.Disruption.DoctorId == doctorId &&
                           item.Disruption.Date == date)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetUserDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Users
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.FullName })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Id, row => row.FullName);
    }
}
