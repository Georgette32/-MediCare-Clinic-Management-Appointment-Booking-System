using MediCare.Application.Interfaces.Persistence;
using MediCare.Web.ViewModels.Doctor;

namespace MediCare.Web.Helpers;

public static class DoctorAppointmentLoader
{
    public static async Task<IReadOnlyList<DoctorAppointmentItem>> LoadAsync(
        IAppointmentRepository appointments,
        Guid doctorProfileId,
        CancellationToken cancellationToken)
    {
        var confirmedAppointments = await appointments.GetConfirmedForDoctorAsync(
            doctorProfileId, cancellationToken);

        var result = new List<DoctorAppointmentItem>();

        foreach (var appointment in confirmedAppointments)
        {
            var patientName = await appointments.GetPatientNameAsync(
                appointment.PatientProfileId, cancellationToken);

            result.Add(new DoctorAppointmentItem(
                appointment.Id,
                patientName ?? "Unknown patient",
                appointment.AppointmentDate,
                appointment.StartTime,
                appointment.EndTime,
                appointment.Reason,
                appointment.Fee));
        }

        return result;
    }
}
