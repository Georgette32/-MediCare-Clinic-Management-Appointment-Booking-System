namespace MediCare.Web.ViewModels.Reception;

public sealed class ScheduledCheckInViewModel
{
    public Guid AppointmentId { get; set; }

    public Guid ExpectedDoctorId { get; set; }
}

public sealed class WalkInRegistrationViewModel
{
    public Guid PatientId { get; set; }

    public Guid DoctorId { get; set; }
}

public sealed class WalkInAdmissionRequestViewModel
{
    public Guid QueueEntryId { get; set; }
}

public sealed class WalkInCallViewModel
{
    public Guid QueueEntryId { get; set; }

    public Guid ApprovalId { get; set; }
}
