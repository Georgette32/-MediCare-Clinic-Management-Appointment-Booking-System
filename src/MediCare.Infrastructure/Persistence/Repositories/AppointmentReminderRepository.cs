
using MediCare.Application.DTOs.Notifications;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MediCare.Infrastructure.Persistence.Repositories;

public sealed class AppointmentReminderRepository : IAppointmentReminderRepository
{
    private readonly ApplicationDbContext _dbContext;

    public AppointmentReminderRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<AppointmentReminderEmailDto>> GetDueRemindersAsync(
        DateTime currentUtc,
        CancellationToken cancellationToken = default)
    {
        var reminders = await (
            from reminder in _dbContext.AppointmentReminders
            join appointment in _dbContext.Appointments
                on reminder.AppointmentId equals appointment.Id
            join patient in _dbContext.PatientProfiles
                on appointment.PatientProfileId equals patient.Id
            join user in _dbContext.Users
                on patient.UserId equals user.Id
            where reminder.Status == AppointmentReminderStatus.Pending
                  && reminder.ScheduledForUtc <= currentUtc
                  && user.Email != null
            orderby reminder.ScheduledForUtc
            select new AppointmentReminderEmailDto
            {
                ReminderId = reminder.Id,
                PatientEmail = user.Email!,
                PatientName = user.FullName,
                AppointmentAtUtc = appointment.ScheduledAtUtc
            })
            .ToListAsync(cancellationToken);

        return reminders;
    }

    public Task<AppointmentReminder?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.AppointmentReminders
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task AddAsync(
        AppointmentReminder reminder,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.AppointmentReminders.AddAsync(
            reminder,
            cancellationToken);
    }
}
