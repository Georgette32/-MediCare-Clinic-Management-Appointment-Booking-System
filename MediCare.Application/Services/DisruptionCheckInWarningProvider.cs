using MediCare.Application.Abstractions;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

/// <summary>Warns at check-in when the appointment is still unresolved on a disruption worklist.</summary>
public sealed class DisruptionCheckInWarningProvider(IDoctorDisruptionRepository disruptions) : ICheckInWarningProvider
{
    public async Task<string?> GetWarningAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var item = await disruptions.GetOpenItemForAppointmentAsync(appointmentId, cancellationToken);
        // A worklist item whose appointment is no longer Confirmed no longer needs a warning.
        if (item is null || item.Appointment?.Status != AppointmentStatus.Confirmed)
            return null;

        return DisruptionWarnings.ForAppointment(item.Disruption.Doctor.FullName, item.Disruption.FromTime, item.Disruption.Reason);
    }
}
