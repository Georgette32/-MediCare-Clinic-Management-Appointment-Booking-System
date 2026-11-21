namespace MediCare.Domain.Entities;

public sealed class Doctor
{
    private Doctor() { }

    public Doctor(Guid id, Guid applicationUserId, string fullName, Guid specializationId, int appointmentDurationMinutes, bool isActive = true)
    {
        if (appointmentDurationMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(appointmentDurationMinutes), "A positive appointment duration must be configured for this doctor.");

        Id = id;
        ApplicationUserId = applicationUserId;
        FullName = fullName;
        SpecializationId = specializationId;
        AppointmentDurationMinutes = appointmentDurationMinutes;
        IsActive = isActive;
    }

    public Guid Id { get; private set; }
    public Guid ApplicationUserId { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public Guid SpecializationId { get; private set; }
    public int AppointmentDurationMinutes { get; private set; }
    public bool IsActive { get; private set; }

    public Specialization Specialization { get; private set; } = null!;
}
