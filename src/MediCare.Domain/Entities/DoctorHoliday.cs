using MediCare.Domain.Common;
using MediCare.Domain.Exceptions;

namespace MediCare.Domain.Entities;

/// <summary>
/// An approved leave period that blocks bookings for the whole date range.
/// </summary>
public sealed class DoctorHoliday : BaseEntity
{
    private DoctorHoliday()
    {
        // Required by EF Core.
    }

    public static DoctorHoliday Create(Guid doctorProfileId, DateOnly startDate, DateOnly endDate, string? reason)
    {
        if (doctorProfileId == Guid.Empty)
        {
            throw new DomainException("A holiday must belong to a doctor.");
        }

        if (endDate < startDate)
        {
            throw new DomainException("Holiday end date cannot be before its start date.");
        }

        return new DoctorHoliday
        {
            DoctorProfileId = doctorProfileId,
            StartDate = startDate,
            EndDate = endDate,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
        };
    }

    public Guid DoctorProfileId { get; private set; }

    public DoctorProfile DoctorProfile { get; private set; } = null!;

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    public string? Reason { get; private set; }
}
