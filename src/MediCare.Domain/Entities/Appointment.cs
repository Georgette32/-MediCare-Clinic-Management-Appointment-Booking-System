using MediCare.Domain.Common;
using MediCare.Domain.Enums;
using MediCare.Domain.Exceptions;

namespace MediCare.Domain.Entities;

/// <summary>
/// Shared appointment aggregate. The booking workflow belongs to the appointments
/// module; the entity is defined here so all modules share one model.
/// </summary>
public sealed class Appointment : BaseEntity
{
    private Appointment()
    {
        // Required by EF Core.
    }

    public Appointment(Guid patientProfileId, Guid doctorProfileId, DateTime scheduledAtUtc, decimal fee, string? reason = null)
    {
        if (patientProfileId == Guid.Empty)
        {
            throw new DomainException("An appointment requires a patient.");
        }

        if (doctorProfileId == Guid.Empty)
        {
            throw new DomainException("An appointment requires a doctor.");
        }

        if (scheduledAtUtc <= DateTime.UtcNow)
        {
            throw new DomainException("An appointment must be scheduled in the future.");
        }

        if (fee < 0)
        {
            throw new DomainException("Appointment fee cannot be negative.");
        }

        PatientProfileId = patientProfileId;
        DoctorProfileId = doctorProfileId;
        ScheduledAtUtc = scheduledAtUtc;
        Fee = decimal.Round(fee, 2);
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Status = AppointmentStatus.Pending;
    }

    public Guid PatientProfileId { get; private set; }

    public PatientProfile PatientProfile { get; private set; } = null!;

    public Guid DoctorProfileId { get; private set; }

    public DoctorProfile DoctorProfile { get; private set; } = null!;

    public DateTime ScheduledAtUtc { get; private set; }

    public AppointmentStatus Status { get; private set; } = AppointmentStatus.Pending;

    public decimal Fee { get; private set; }

    public string? Reason { get; private set; }

    public string? DoctorNotes { get; private set; }

    public void Confirm()
    {
        if (Status != AppointmentStatus.Pending)
        {
            throw new DomainException("Only pending appointments can be confirmed.");
        }

        Status = AppointmentStatus.Confirmed;
        MarkUpdated();
    }

    public void Cancel()
    {
        if (Status is AppointmentStatus.Completed or AppointmentStatus.Cancelled)
        {
            throw new DomainException("This appointment can no longer be cancelled.");
        }

        Status = AppointmentStatus.Cancelled;
        MarkUpdated();
    }

    public void Complete(string? doctorNotes)
    {
        if (Status != AppointmentStatus.Confirmed)
        {
            throw new DomainException("Only confirmed appointments can be completed.");
        }

        Status = AppointmentStatus.Completed;
        DoctorNotes = string.IsNullOrWhiteSpace(doctorNotes) ? null : doctorNotes.Trim();
        MarkUpdated();
    }

    public void MarkNoShow()
    {
        if (Status != AppointmentStatus.Confirmed)
        {
            throw new DomainException("Only confirmed appointments can be marked as a no-show.");
        }

        Status = AppointmentStatus.NoShow;
        MarkUpdated();
    }
}
