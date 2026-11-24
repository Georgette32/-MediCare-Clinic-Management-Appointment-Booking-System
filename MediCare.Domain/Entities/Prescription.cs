namespace MediCare.Domain.Entities;

/// <summary>The prescription of one medical record: one prescription per record, with at least one item.</summary>
public sealed class Prescription
{
    public const int MaxNotesLength = 1000;

    private readonly List<PrescriptionItem> _items = [];

    private Prescription() { }

    public Guid Id { get; private set; }
    public Guid MedicalRecordId { get; private set; }
    public DateTimeOffset IssuedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public string? Notes { get; private set; }

    public IReadOnlyCollection<PrescriptionItem> Items => _items;

    internal static Prescription Create(Guid medicalRecordId, IReadOnlyList<PrescriptionItemDraft> items, string? notes, DateTimeOffset nowUtc)
    {
        var prescription = new Prescription
        {
            Id = Guid.NewGuid(),
            MedicalRecordId = medicalRecordId,
            IssuedAtUtc = nowUtc.ToUniversalTime()
        };
        prescription.Replace(items, notes, nowUtc);
        return prescription;
    }

    /// <summary>Replaces every item and the notes. A prescription always keeps at least one item.</summary>
    internal void Replace(IReadOnlyList<PrescriptionItemDraft> items, string? notes, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            throw new ArgumentException("A prescription needs at least one item.", nameof(items));

        var trimmedNotes = MedicalRecord.Normalize(notes);
        if (trimmedNotes is not null && trimmedNotes.Length > MaxNotesLength)
            throw new ArgumentException($"The prescription notes cannot exceed {MaxNotesLength} characters.", nameof(notes));

        var built = items.Select(draft => PrescriptionItem.Create(Id, draft)).ToList();
        _items.Clear();
        _items.AddRange(built);
        Notes = trimmedNotes;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    /// <summary>
    /// Items active on the clinic date. An item is active while the date is no later than the issue date plus
    /// DurationDays - 1. Prescriptions are written on the visit day, so the issue clinic date is the record's visit date.
    /// </summary>
    public IReadOnlyList<PrescriptionItem> ActiveItemsOn(DateOnly issuedClinicDate, DateOnly clinicToday) =>
        _items.Where(item => item.IsActiveOn(issuedClinicDate, clinicToday)).ToArray();
}
