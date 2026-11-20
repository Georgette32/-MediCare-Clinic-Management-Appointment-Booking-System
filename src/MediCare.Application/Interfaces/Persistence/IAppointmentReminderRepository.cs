
using MediCare.Application.DTOs.Notifications;
using MediCare.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MediCare.Application.Interfaces.Persistence;

public interface IAppointmentReminderRepository
{
    Task<IReadOnlyList<AppointmentReminderEmailDto>> GetDueRemindersAsync(
        DateTime currentUtc,
        CancellationToken cancellationToken = default);

    Task<AppointmentReminder?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        AppointmentReminder reminder,
        CancellationToken cancellationToken = default);
}
