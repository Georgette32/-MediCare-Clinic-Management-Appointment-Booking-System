namespace MediCare.Domain.Entities;

/// <summary>
/// Clinic-wide settings. The table holds exactly one row whose Id is <see cref="SingletonId"/>. Nothing is seeded:
/// an unset minimum sample size (null) means the expected-duration fallback applies.
/// </summary>
public sealed class ClinicSettings
{
    public const int SingletonId = 1;

    private ClinicSettings() { }

    public static ClinicSettings CreateEmpty(DateTimeOffset updatedAtUtc) =>
        new() { Id = SingletonId, UpdatedAtUtc = updatedAtUtc.ToUniversalTime() };

    public int Id { get; private set; }
    public int? MinimumConsultationSampleSize { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }

    /// <summary>Sets or clears (null) the sample size required before historical averages are used.</summary>
    public void SetMinimumConsultationSampleSize(int? value, Guid actorUserId, DateTimeOffset updatedAtUtc)
    {
        if (value is not null && value.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), "The minimum consultation sample size must be a positive number, or empty to clear it.");
        if (actorUserId == Guid.Empty)
            throw new ArgumentException("A settings change requires the acting user.", nameof(actorUserId));

        MinimumConsultationSampleSize = value;
        UpdatedByUserId = actorUserId;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }
}
