namespace MediCare.Web.ViewModels.Doctor;

public sealed class DoctorAppointmentsViewModel
{
    public IReadOnlyList<DoctorAppointmentItem> Appointments { get; set; } = Array.Empty<DoctorAppointmentItem>();
}

public sealed record DoctorAppointmentItem(
    Guid Id,
    string PatientName,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Reason,
    decimal Fee);
