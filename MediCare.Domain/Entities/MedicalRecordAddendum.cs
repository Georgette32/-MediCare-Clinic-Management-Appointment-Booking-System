namespace MediCare.Domain.Entities;

/// <summary>A dated correction or addition to a locked visit record. Written by a treating doctor after the visit day.</summary>
public sealed class MedicalRecordAddendum
{
    public const int MaxTextLength = 2000;

    private MedicalRecordAddendum() { }

    public Guid Id { get; private set; }
    public Guid MedicalRecordId { get; private set; }
    public Guid DoctorId { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Doctor Doctor { get; private set; } = null!;

    internal static MedicalRecordAddendum Create(Guid medicalRecordId, Guid doctorId, string text, DateTimeOffset nowUtc)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException("An addendum cannot be empty.", nameof(text));
        if (trimmed.Length > MaxTextLength)
            throw new ArgumentException($"An addendum cannot exceed {MaxTextLength} characters.", nameof(text));

        return new MedicalRecordAddendum
        {
            Id = Guid.NewGuid(),
            MedicalRecordId = medicalRecordId,
            DoctorId = doctorId,
            Text = trimmed,
            CreatedAtUtc = nowUtc.ToUniversalTime()
        };
    }
}
