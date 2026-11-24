namespace MediCare.Web.Models;

public sealed class ScheduledCheckInViewModel
{
    public Guid AppointmentId { get; set; }
    public Guid ExpectedDoctorId { get; set; }
}
