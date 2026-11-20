
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MediCare.Application.Services;

public sealed class AppointmentReminderService : IAppointmentReminderService
{
    private readonly IAppointmentReminderRepository _reminders;
    private readonly IEmailService _emailService;
    private readonly IUnitOfWork _unitOfWork;

    public AppointmentReminderService(
        IAppointmentReminderRepository reminders,
        IEmailService emailService,
        IUnitOfWork unitOfWork)
    {
        _reminders = reminders;
        _emailService = emailService;
        _unitOfWork = unitOfWork;
    }

    public async Task ProcessDueRemindersAsync(
        CancellationToken cancellationToken = default)
    {
        var dueReminders = await _reminders.GetDueRemindersAsync(
            DateTime.UtcNow,
            cancellationToken);

        foreach (var reminderData in dueReminders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var reminder = await _reminders.GetByIdAsync(
                reminderData.ReminderId,
                cancellationToken);

            if (reminder is null)
                continue;

            var patientName = WebUtility.HtmlEncode(reminderData.PatientName);

            var appointmentDate = reminderData.AppointmentAtUtc
                .ToString("dd/MM/yyyy hh:mm tt");

            var subject = "MediCare - Appointment Reminder";

            var body = $"""
                <h2>Hello {patientName},</h2>
                <p>This is a friendly reminder about your upcoming appointment.</p>
                <p><strong>Appointment Date:</strong> {appointmentDate} UTC</p>
                <p>We look forward to seeing you.</p>
                <p>Best regards,<br/>MediCare Team</p>
                """;

            try
            {
                await _emailService.SendAsync(
                    reminderData.PatientEmail,
                    subject,
                    body,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                reminder.MarkAsFailed();

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                continue;
            }

            reminder.MarkAsSent();

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

