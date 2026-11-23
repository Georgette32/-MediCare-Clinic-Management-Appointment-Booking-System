using MediCare.Domain.Common;
using MediCare.Domain.Exceptions;

namespace MediCare.Domain.Entities;

/// <summary>
/// A date-specific break during which a doctor cannot accept appointments.
/// </summary>
public sealed class DoctorBreak : BaseEntity
{
    private DoctorBreak()
    {
        // Required by EF Core.
    }

    public static DoctorBreak Create(Guid doctorProfileId, DateOnly date, TimeOnly startTime, TimeOnly endTime)
    {
        if (doctorProfileId == Guid.Empty)
        {
            throw new DomainException("A break must belong to a doctor.");
        }

        if (endTime <= startTime)
        {
            throw new DomainException("Break end must be later than its start.");
        }

        return new DoctorBreak
        {
            DoctorProfileId = doctorProfileId,
            Date = date,
            StartTime = startTime,
            EndTime = endTime
        };
    }

    public Guid DoctorProfileId { get; private set; }

    public DoctorProfile DoctorProfile { get; private set; } = null!;

    public DateOnly Date { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }
}
