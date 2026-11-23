using MediCare.Domain.Common;
using MediCare.Domain.Exceptions;

namespace MediCare.Domain.Entities;

/// <summary>
/// Recurring working window for a doctor, used by the booking service to validate
/// that requested slots fall inside an active schedule.
/// </summary>
public sealed class DoctorWorkingHour : BaseEntity
{
    private DoctorWorkingHour()
    {
        // Required by EF Core.
    }

    public static DoctorWorkingHour Create(Guid doctorProfileId, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime)
    {
        if (doctorProfileId == Guid.Empty)
        {
            throw new DomainException("A working hour must belong to a doctor.");
        }

        if (endTime <= startTime)
        {
            throw new DomainException("Working-hour end must be later than its start.");
        }

        return new DoctorWorkingHour
        {
            DoctorProfileId = doctorProfileId,
            DayOfWeek = dayOfWeek,
            StartTime = startTime,
            EndTime = endTime,
            IsActive = true
        };
    }

    public Guid DoctorProfileId { get; private set; }

    public DoctorProfile DoctorProfile { get; private set; } = null!;

    public DayOfWeek DayOfWeek { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public bool IsActive { get; private set; }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        MarkUpdated();
    }

    public void Update(DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime)
    {
        if (endTime <= startTime)
        {
            throw new DomainException("Working-hour end must be later than its start.");
        }

        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;
        MarkUpdated();
    }
}
