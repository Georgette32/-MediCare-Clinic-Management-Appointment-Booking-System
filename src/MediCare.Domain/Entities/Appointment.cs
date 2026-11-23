using MediCare.Domain.Common;
using MediCare.Domain.Enums;
using MediCare.Domain.Exceptions;

namespace MediCare.Domain.Entities;

/// <summary>
/// Shared appointment aggregate. It carries both the shared scheduling foundation
/// used by dashboards and the booking lifecycle used by the appointments module.
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
        AppointmentDate = DateOnly.FromDateTime(scheduledAtUtc);
        StartTime = TimeOnly.FromDateTime(scheduledAtUtc);
        Fee = decimal.Round(fee, 2);
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Status = AppointmentStatus.Pending;
    }

    public static Appointment Request(
        Guid patientProfileId,
        Guid doctorProfileId,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        DateTime createdAtUtc,
        Guid? rescheduledFromAppointmentId = null)
    {
        if (patientProfileId == Guid.Empty)
        {
            throw new DomainException("An appointment requires a patient.");
        }

        if (doctorProfileId == Guid.Empty)
        {
            throw new DomainException("An appointment requires a doctor.");
        }

        if (end <= start)
        {
            throw new DomainException("Appointment end must be later than its start.");
        }

        var appointment = new Appointment
        {
            PatientProfileId = patientProfileId,
            DoctorProfileId = doctorProfileId,
            RescheduledFromAppointmentId = rescheduledFromAppointmentId,
            AppointmentDate = date,
            StartTime = start,
            EndTime = end,
            ScheduledAtUtc = date.ToDateTime(start, DateTimeKind.Utc),
            Status = AppointmentStatus.Pending
        };
        appointment.SetCreatedAtUtc(createdAtUtc);
        return appointment;
    }

    public Guid PatientProfileId { get; private set; }

    public PatientProfile PatientProfile { get; private set; } = null!;

    public Guid DoctorProfileId { get; private set; }

    public DoctorProfile DoctorProfile { get; private set; } = null!;

    public Guid? RescheduledFromAppointmentId { get; private set; }

    public Appointment? RescheduledFromAppointment { get; private set; }

    public DateOnly AppointmentDate { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public DateTime ScheduledAtUtc { get; private set; }

    public AppointmentStatus Status { get; private set; } = AppointmentStatus.Pending;

    public decimal Fee { get; private set; }

    public string? Reason { get; private set; }

    public string? CancellationReason { get; private set; }

    public int? FrozenGracePeriodMinutes { get; private set; }

    public DateTime? GracePeriodStartedAtUtc { get; private set; }

    public string? DoctorNotes { get; private set; }

    // --- Shared lifecycle (used by dashboards and the existing application) ---

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

    // --- Booking module lifecycle ---

    public void Confirm(DateTime updatedAtUtc)
    {
        EnsurePending();
        Status = AppointmentStatus.Confirmed;
        MarkUpdatedAt(updatedAtUtc);
    }

    public void Reject(DateTime updatedAtUtc)
    {
        EnsurePending();
        Status = AppointmentStatus.Rejected;
        MarkUpdatedAt(updatedAtUtc);
    }

    public void CancelByPatient(DateOnly clinicDate, DateTime updatedAtUtc, string? reason = null)
    {
        if (clinicDate >= AppointmentDate)
        {
            throw new DomainException("Patients may cancel only before the appointment day.");
        }

        if (Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed))
        {
            throw new DomainException("Only pending or confirmed appointments can be cancelled.");
        }

        Status = AppointmentStatus.Cancelled;
        CancellationReason = reason;
        MarkUpdatedAt(updatedAtUtc);
    }

    public void CancelByClinic(string reason, DateTime updatedAtUtc)
    {
        if (Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed))
        {
            throw new DomainException("Only pending or confirmed appointments can be cancelled.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A cancellation reason is required.");
        }

        Status = AppointmentStatus.Cancelled;
        CancellationReason = reason.Trim();
        MarkUpdatedAt(updatedAtUtc);
    }

    public Appointment CreateRescheduledReplacement(
        Guid doctorProfileId,
        DateOnly newDate,
        TimeOnly newStart,
        TimeOnly newEnd,
        DateTime createdAtUtc)
    {
        EnsureConfirmedForReplacement();
        return Request(PatientProfileId, doctorProfileId, newDate, newStart, newEnd, createdAtUtc, Id);
    }

    public Appointment ReplaceByClinic(
        Guid doctorProfileId,
        DateOnly newDate,
        TimeOnly newStart,
        TimeOnly newEnd,
        DateTime updatedAtUtc)
    {
        EnsureConfirmedForReplacement();
        var replacement = Request(PatientProfileId, doctorProfileId, newDate, newStart, newEnd, updatedAtUtc, Id);
        replacement.Confirm(updatedAtUtc);
        Status = AppointmentStatus.Rescheduled;
        MarkUpdatedAt(updatedAtUtc);
        return replacement;
    }

    public void MarkRescheduled(DateTime updatedAtUtc)
    {
        EnsureConfirmedForReplacement();
        Status = AppointmentStatus.Rescheduled;
        MarkUpdatedAt(updatedAtUtc);
    }

    public void StartGracePeriod(int gracePeriodMinutes, DateTime startedAtUtc)
    {
        if (Status != AppointmentStatus.Confirmed)
        {
            throw new DomainException("Grace period can be started only for confirmed appointments.");
        }

        if (gracePeriodMinutes <= 0)
        {
            throw new DomainException("Grace period minutes must be positive.");
        }

        if (FrozenGracePeriodMinutes is not null)
        {
            throw new DomainException("Grace period has already been started for this appointment.");
        }

        FrozenGracePeriodMinutes = gracePeriodMinutes;
        GracePeriodStartedAtUtc = startedAtUtc.ToUniversalTime();
        MarkUpdatedAt(startedAtUtc);
    }

    public bool IsGracePeriodExpired(DateTime currentUtc)
    {
        if (GracePeriodStartedAtUtc is null || FrozenGracePeriodMinutes is null)
        {
            return false;
        }

        return currentUtc >= GracePeriodStartedAtUtc.Value.AddMinutes(FrozenGracePeriodMinutes.Value);
    }

    public void MarkNoShow(DateTime updatedAtUtc)
    {
        if (Status != AppointmentStatus.Confirmed)
        {
            throw new DomainException("Only confirmed appointments can be marked as a no-show.");
        }

        Status = AppointmentStatus.NoShow;
        MarkUpdatedAt(updatedAtUtc);
    }

    private void EnsureConfirmedForReplacement()
    {
        if (Status != AppointmentStatus.Confirmed)
        {
            throw new DomainException("Only confirmed appointments can be replaced by rescheduling.");
        }
    }

    private void EnsurePending()
    {
        if (Status != AppointmentStatus.Pending)
        {
            throw new DomainException("Only pending appointments can be confirmed or rejected.");
        }
    }

    private void MarkUpdatedAt(DateTime updatedAtUtc) => SetUpdatedAtUtc(updatedAtUtc);
}
