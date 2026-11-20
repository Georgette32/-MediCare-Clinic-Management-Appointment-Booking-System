using MediCare.Domain.Common;
using MediCare.Domain.Exceptions;

namespace MediCare.Domain.Entities;

/// <summary>
/// Recurring weekly availability window. Appointment booking is implemented by the
/// scheduling module; this entity is shared so both modules agree on the shape.
/// </summary>
public sealed class DoctorAvailability : BaseEntity
{
    private DoctorAvailability()
    {
        // Required by EF Core.
    }

    public DoctorAvailability(Guid doctorProfileId, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime, int slotDurationMinutes = 30)
    {
        if (doctorProfileId == Guid.Empty)
        {
            throw new DomainException("Availability must belong to a doctor profile.");
        }

        if (startTime >= endTime)
        {
            throw new DomainException("Availability start time must be before the end time.");
        }

        if (slotDurationMinutes is < 10 or > 120)
        {
            throw new DomainException("Slot duration must be between 10 and 120 minutes.");
        }

        DoctorProfileId = doctorProfileId;
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;
        SlotDurationMinutes = slotDurationMinutes;
    }

    public Guid DoctorProfileId { get; private set; }

    public DoctorProfile DoctorProfile { get; private set; } = null!;

    public DayOfWeek DayOfWeek { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public int SlotDurationMinutes { get; private set; } = 30;

    public bool IsActive { get; private set; } = true;

    public void Update(DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime, int slotDurationMinutes)
    {
        if (startTime >= endTime)
        {
            throw new DomainException("Availability start time must be before the end time.");
        }

        if (slotDurationMinutes is < 10 or > 120)
        {
            throw new DomainException("Slot duration must be between 10 and 120 minutes.");
        }

        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;
        SlotDurationMinutes = slotDurationMinutes;
        MarkUpdated();
    }

    public void Activate()
    {
        IsActive = true;
        MarkUpdated();
    }

    public void Deactivate()
    {
        IsActive = false;
        MarkUpdated();
    }
}
