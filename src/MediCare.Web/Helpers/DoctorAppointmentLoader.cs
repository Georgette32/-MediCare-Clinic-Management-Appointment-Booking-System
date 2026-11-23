using MediCare.Application.Interfaces.Persistence;
using MediCare.Web.ViewModels.Doctor;

namespace MediCare.Web.Helpers;

public static class DoctorAppointmentLoader
{
    public static async Task<IReadOnlyList<DoctorAppointmentItem>> LoadAsync(
        IAppointmentRepository appointments,
        Guid doctorProfileId,
        DateOnly fromDate,
        int days,
        CancellationToken cancellationToken)
    {
        var result = new List<DoctorAppointmentItem>();

        for (var offset = 0; offset < days; offset++)
        {
            var date = fromDate.AddDays(offset);
            var appointmentsForDate = await appointments.GetConfirmedForDoctorAndDateAsync(
                doctorProfileId, date, cancellationToken);

            foreach (var appointment in appointmentsForDate)
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
        }

        return result;
    }
}
