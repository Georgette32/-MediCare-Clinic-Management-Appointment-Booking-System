using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

public sealed class MedicalRecordDomainTests
{
    private static readonly DateOnly Today = new(2026, 12, 1);
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid OtherDoctor = Guid.NewGuid();

    [Theory]
    [InlineData(QueueEntryStatus.Waiting)]
    [InlineData(QueueEntryStatus.Called)]
    [InlineData(QueueEntryStatus.Cancelled)]
    [InlineData(QueueEntryStatus.NoShow)]
    public void A_record_cannot_be_created_before_or_outside_a_consultation(QueueEntryStatus status)
    {
        var entry = Entry(status, Author, Today);

        Assert.Throws<InvalidOperationException>(() => MedicalRecord.CreateForVisit(entry, Author, null, "Flu", null, Noon));
    }

    [Theory]
    [InlineData(QueueEntryStatus.InConsultation)]
    [InlineData(QueueEntryStatus.Completed)]
    public void A_record_is_created_for_an_in_consultation_or_completed_attempt_with_the_entry_facts(QueueEntryStatus status)
    {
        var entry = Entry(status, Author, Today);

        var record = MedicalRecord.CreateForVisit(entry, Author, "  Cough  ", " Bronchitis ", "  rest  ", Noon);

        Assert.Equal(entry.PatientId, record.PatientId);
        Assert.Equal(Author, record.DoctorId);
        Assert.Equal(entry.Id, record.QueueEntryId);
        Assert.Equal(entry.AppointmentId, record.AppointmentId);
        Assert.Equal(Today, record.VisitDate);
        Assert.Equal("Cough", record.ChiefComplaint);
        Assert.Equal("Bronchitis", record.Diagnosis);
        Assert.Equal("rest", record.ClinicalNotes);
        Assert.Equal(Noon, record.CreatedAtUtc);
    }

    [Fact]
    public void Only_the_doctor_of_the_entry_can_create_the_record()
    {
        var entry = Entry(QueueEntryStatus.InConsultation, Author, Today);

        Assert.Throws<InvalidOperationException>(() => MedicalRecord.CreateForVisit(entry, OtherDoctor, null, "Flu", null, Noon));
    }

    [Fact]
    public void Diagnosis_is_required_and_bounded_and_optional_text_is_blank_normalised()
    {
        var entry = Entry(QueueEntryStatus.InConsultation, Author, Today);

        Assert.Throws<ArgumentException>(() => MedicalRecord.CreateForVisit(entry, Author, null, "   ", null, Noon));
        Assert.Throws<ArgumentException>(() => MedicalRecord.CreateForVisit(entry, Author, null, new string('d', MedicalRecord.MaxDiagnosisLength + 1), null, Noon));
        Assert.Throws<ArgumentException>(() => MedicalRecord.CreateForVisit(entry, Author, new string('c', MedicalRecord.MaxChiefComplaintLength + 1), "x", null, Noon));
        Assert.Throws<ArgumentException>(() => MedicalRecord.CreateForVisit(entry, Author, null, "x", new string('n', MedicalRecord.MaxClinicalNotesLength + 1), Noon));

        var record = MedicalRecord.CreateForVisit(entry, Author, "   ", "x", "   ", Noon);
        Assert.Null(record.ChiefComplaint);
        Assert.Null(record.ClinicalNotes);
    }

    [Fact]
    public void The_authoring_doctor_can_edit_only_on_the_visit_clinic_day()
    {
        var record = Record(Today);

        record.UpdateVisit(Author, Today, "Fever", "Viral infection", null, Noon.AddHours(1));
        Assert.Equal("Viral infection", record.Diagnosis);
        Assert.Equal(Noon.AddHours(1), record.UpdatedAtUtc);

        Assert.Throws<InvalidOperationException>(() => record.UpdateVisit(OtherDoctor, Today, null, "Other", null, Noon));
        Assert.Throws<InvalidOperationException>(() => record.UpdateVisit(Author, Today.AddDays(1), null, "Later", null, Noon));
        Assert.Equal("Viral infection", record.Diagnosis);
    }

    [Fact]
    public void After_the_visit_day_the_record_is_locked_and_corrections_are_addenda()
    {
        var record = Record(Today);
        var nextDay = Today.AddDays(1);

        Assert.False(record.IsEditableBy(Author, nextDay));
        Assert.Throws<InvalidOperationException>(() => record.UpdateVisit(Author, nextDay, null, "Changed", null, Noon));
        Assert.Throws<InvalidOperationException>(() => record.AddAddendum(Author, Today, "Too early", Noon));

        var addendum = record.AddAddendum(OtherDoctor, nextDay, "  Lab result received  ", Noon.AddDays(1));

        Assert.Equal("Lab result received", addendum.Text);
        Assert.Equal(OtherDoctor, addendum.DoctorId);
        Assert.Single(record.Addenda);
        Assert.Equal("Viral infection", record.Diagnosis);
    }

    [Fact]
    public void Addenda_must_be_non_blank_and_bounded()
    {
        var record = Record(Today);
        var nextDay = Today.AddDays(1);

        Assert.Throws<ArgumentException>(() => record.AddAddendum(Author, nextDay, "  ", Noon));
        Assert.Throws<ArgumentException>(() => record.AddAddendum(Author, nextDay, new string('a', MedicalRecordAddendum.MaxTextLength + 1), Noon));
        Assert.Empty(record.Addenda);
    }

    [Fact]
    public void A_prescription_needs_at_least_one_item_and_valid_items()
    {
        var record = Record(Today);

        Assert.Throws<ArgumentException>(() => record.SetPrescription(Author, Today, [], null, Noon));
        Assert.Throws<ArgumentOutOfRangeException>(() => record.SetPrescription(Author, Today, [Draft(durationDays: 0)], null, Noon));
        Assert.Throws<ArgumentException>(() => record.SetPrescription(Author, Today, [Draft(name: " ")], null, Noon));
        Assert.Throws<ArgumentException>(() => record.SetPrescription(Author, Today, [Draft(dosage: new string('d', 101))], null, Noon));
        Assert.Throws<ArgumentException>(() => record.SetPrescription(Author, Today, [Draft(frequency: null)], null, Noon));
        Assert.Throws<ArgumentException>(() => record.SetPrescription(Author, Today, [Draft(instructions: new string('i', 501))], null, Noon));
        Assert.Null(record.Prescription);
    }

    [Fact]
    public void Replacing_a_prescription_swaps_all_items_and_a_bad_replacement_leaves_the_old_items_untouched()
    {
        var record = Record(Today);
        record.SetPrescription(Author, Today, [Draft(name: "Amoxicillin"), Draft(name: "Paracetamol")], "Take with food", Noon);
        Assert.Equal(2, record.Prescription!.Items.Count);

        Assert.Throws<ArgumentOutOfRangeException>(() => record.SetPrescription(Author, Today, [Draft(name: "New"), Draft(durationDays: -1)], null, Noon));
        Assert.Equal(2, record.Prescription!.Items.Count);
        Assert.Equal("Take with food", record.Prescription.Notes);

        var replaced = record.SetPrescription(Author, Today, [Draft(name: "Ibuprofen")], null, Noon.AddMinutes(5));
        Assert.Single(replaced.Items);
        Assert.Equal("Ibuprofen", replaced.Items.Single().MedicationName);
        Assert.Null(replaced.Notes);
        Assert.Same(record.Prescription, replaced);
    }

    [Fact]
    public void Prescriptions_are_editable_only_by_the_author_on_the_visit_day()
    {
        var record = Record(Today);

        Assert.Throws<InvalidOperationException>(() => record.SetPrescription(OtherDoctor, Today, [Draft()], null, Noon));
        Assert.Throws<InvalidOperationException>(() => record.SetPrescription(Author, Today.AddDays(1), [Draft()], null, Noon));
        Assert.Null(record.Prescription);
    }

    [Fact]
    public void An_item_is_active_from_the_visit_day_through_duration_minus_one_days()
    {
        var record = Record(Today);
        var prescription = record.SetPrescription(Author, Today, [Draft(durationDays: 3, name: "Amoxicillin"), Draft(durationDays: 1, name: "Vitamin C")], null, Noon);

        Assert.Equal(2, prescription.ActiveItemsOn(Today, Today).Count);
        Assert.Single(prescription.ActiveItemsOn(Today, Today.AddDays(1)));
        var active = prescription.ActiveItemsOn(Today, Today.AddDays(2));
        Assert.Equal("Amoxicillin", Assert.Single(active).MedicationName);
        Assert.Empty(prescription.ActiveItemsOn(Today, Today.AddDays(3)));
        Assert.Empty(prescription.ActiveItemsOn(Today, Today.AddDays(-1)));
    }

    private static readonly DateTimeOffset Noon = new(2026, 12, 1, 12, 0, 0, TimeSpan.Zero);

    private static PrescriptionItemDraft Draft(string? name = "Amoxicillin", string? dosage = "500 mg", string? frequency = "Twice daily", int durationDays = 5, string? instructions = null) =>
        new(name, dosage, frequency, durationDays, instructions);

    private static MedicalRecord Record(DateOnly visitDate)
    {
        var entry = Entry(QueueEntryStatus.InConsultation, Author, visitDate);
        return MedicalRecord.CreateForVisit(entry, Author, "Cough", "Viral infection", null, Noon);
    }

    private static QueueEntry Entry(QueueEntryStatus status, Guid doctorId, DateOnly date)
    {
        var arrived = new DateTimeOffset(date.ToDateTime(new TimeOnly(8, 55)), TimeSpan.Zero);
        var entry = QueueEntry.CreateScheduled(Guid.NewGuid(), doctorId, Guid.NewGuid(), date, arrived);
        switch (status)
        {
            case QueueEntryStatus.Waiting:
                break;
            case QueueEntryStatus.Called:
                entry.Call(arrived.AddMinutes(5));
                break;
            case QueueEntryStatus.InConsultation:
                entry.Call(arrived.AddMinutes(5));
                entry.StartConsultation(arrived.AddMinutes(10));
                break;
            case QueueEntryStatus.Completed:
                entry.Call(arrived.AddMinutes(5));
                entry.StartConsultation(arrived.AddMinutes(10));
                entry.Complete(arrived.AddMinutes(20));
                break;
            case QueueEntryStatus.Cancelled:
                entry.Cancel(arrived.AddMinutes(5));
                break;
            case QueueEntryStatus.NoShow:
                entry.MarkNoShow();
                break;
        }
        return entry;
    }
}
