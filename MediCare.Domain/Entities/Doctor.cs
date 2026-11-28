namespace MediCare.Domain.Entities;

public sealed class Doctor
{
    public const int MaxBiographyLength = 1000;
    public const int MaxFullNameLength = 200;

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
    public string? Biography { get; private set; }

    public int? NextAppointmentDurationMinutes { get; private set; }
    public DateOnly? NextDurationEffectiveFrom { get; private set; }

    public Specialization Specialization { get; private set; } = null!;

    /// <summary>The slot length in force on a clinic-local date: the pending duration from its effective date on, otherwise the current one.</summary>
    public int DurationOn(DateOnly date) =>
        NextAppointmentDurationMinutes is { } next && NextDurationEffectiveFrom is { } effectiveFrom && date >= effectiveFrom
            ? next
            : AppointmentDurationMinutes;

    /// <summary>
    /// Schedules a slot-length change from a future clinic-local date. A value equal to the duration already in force
    /// the day before clears any pending change; a new call replaces a pending change.
    /// </summary>
    public void ScheduleDurationChange(int minutes, DateOnly effectiveFrom, DateOnly clinicToday)
    {
        if (minutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(minutes), "An appointment duration must be a positive number of minutes.");
        if (effectiveFrom <= clinicToday)
            throw new ArgumentOutOfRangeException(nameof(effectiveFrom), "A scheduled duration change must take effect after today.");

        if (minutes == DurationOn(effectiveFrom.AddDays(-1)))
        {
            CancelScheduledDurationChange();
            return;
        }

        NextAppointmentDurationMinutes = minutes;
        NextDurationEffectiveFrom = effectiveFrom;
    }

    public void CancelScheduledDurationChange()
    {
        NextAppointmentDurationMinutes = null;
        NextDurationEffectiveFrom = null;
    }

    /// <summary>Makes a due pending change the current duration. Returns false when nothing is due on <paramref name="clinicToday"/>.</summary>
    public bool PromoteDueDurationChange(DateOnly clinicToday)
    {
        if (NextAppointmentDurationMinutes is not { } minutes || NextDurationEffectiveFrom is not { } effectiveFrom || effectiveFrom > clinicToday)
            return false;

        AppointmentDurationMinutes = minutes;
        CancelScheduledDurationChange();
        return true;
    }

    /// <summary>Changes the slot length. The caller must first prove that no live appointment conflicts with the new grid.</summary>
    public void ChangeAppointmentDuration(int minutes)
    {
        if (minutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(minutes), "An appointment duration must be a positive number of minutes.");
        AppointmentDurationMinutes = minutes;
    }

    /// <summary>Sets the public profile text. Blank input clears it; text longer than <see cref="MaxBiographyLength"/> is refused.</summary>
    public void UpdateBiography(string? biography)
    {
        var trimmed = biography?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            Biography = null;
            return;
        }
        if (trimmed.Length > MaxBiographyLength)
            throw new ArgumentException($"The biography cannot exceed {MaxBiographyLength} characters.", nameof(biography));
        Biography = trimmed;
    }

    /// <summary>Changes the public display name. Blank names and names longer than <see cref="MaxFullNameLength"/> are refused.</summary>
    public void ChangeFullName(string fullName)
    {
        var trimmed = fullName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException("A full name is required.", nameof(fullName));
        if (trimmed.Length > MaxFullNameLength)
            throw new ArgumentException($"The full name cannot exceed {MaxFullNameLength} characters.", nameof(fullName));
        FullName = trimmed;
    }

    /// <summary>Hides the doctor from discovery and booking. Existing appointments are untouched.</summary>
    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
