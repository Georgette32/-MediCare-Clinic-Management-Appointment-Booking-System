using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

/// <summary>Medical service behaviour: ownership resolved from the signed-in user, lock rules, and the read policy.</summary>
public sealed class MedicalRecordServiceTests
{
    private static readonly DateOnly Today = QueueTestData.Today;
    private static readonly DateOnly Yesterday = Today.AddDays(-1);

    [Fact]
    public async Task The_entry_doctor_creates_one_record_and_updates_it_on_the_same_day()
    {
        var w = new World();
        var entry = w.Visit(w.Patient, w.Doctor, Today);

        var created = await w.Service.CreateOrUpdateForVisitAsync(entry.Id, w.Doctor.ApplicationUserId, Visit("Cough", "Bronchitis"), CancellationToken.None);
        var updated = await w.Service.CreateOrUpdateForVisitAsync(entry.Id, w.Doctor.ApplicationUserId, Visit("Cough", "Acute bronchitis"), CancellationToken.None);

        Assert.True(created.Succeeded);
        Assert.True(updated.Succeeded);
        Assert.Equal(created.RecordId, updated.RecordId);
        Assert.Single(w.Records.Records);
        Assert.Equal("Acute bronchitis", w.Records.Records.Values.Single().Diagnosis);
    }

    [Fact]
    public async Task Another_doctor_cannot_create_a_record_for_someone_elses_patient_even_by_naming_the_entry()
    {
        var w = new World();
        var entry = w.Visit(w.Patient, w.Doctor, Today);

        var result = await w.Service.CreateOrUpdateForVisitAsync(entry.Id, w.OtherDoctor.ApplicationUserId, Visit("Cough", "Flu"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(w.Records.Records);
    }

    [Fact]
    public async Task An_account_without_an_active_doctor_profile_cannot_write_visit_records()
    {
        var w = new World();
        var entry = w.Visit(w.Patient, w.Doctor, Today);

        var result = await w.Service.CreateOrUpdateForVisitAsync(entry.Id, Guid.NewGuid(), Visit("Cough", "Flu"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not linked", result.Message);
    }

    [Fact]
    public async Task A_waiting_attempt_cannot_get_a_visit_record()
    {
        var w = new World();
        var entry = w.Visit(w.Patient, w.Doctor, Today, inConsultation: false);

        var result = await w.Service.CreateOrUpdateForVisitAsync(entry.Id, w.Doctor.ApplicationUserId, Visit("Cough", "Flu"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("in progress or completed", result.Message);
        Assert.Empty(w.Records.Records);
    }

    [Fact]
    public async Task After_the_visit_day_the_visit_text_is_locked_and_addenda_are_the_only_correction()
    {
        var w = new World();
        var entry = w.Visit(w.Patient, w.Doctor, Yesterday);
        var created = await w.Service.CreateOrUpdateForVisitAsync(entry.Id, w.Doctor.ApplicationUserId, Visit("Cough", "Flu"), CancellationToken.None);

        var rewrite = await w.Service.CreateOrUpdateForVisitAsync(entry.Id, w.Doctor.ApplicationUserId, Visit("Cough", "Pneumonia"), CancellationToken.None);
        var addendum = await w.Service.AddAddendumAsync(created.RecordId!.Value, w.Doctor.ApplicationUserId, "Chest X-ray normal", CancellationToken.None);

        Assert.False(rewrite.Succeeded);
        Assert.Contains("locked", rewrite.Message);
        Assert.True(addendum.Succeeded);
        var record = w.Records.Records.Values.Single();
        Assert.Equal("Flu", record.Diagnosis);
        Assert.Equal("Chest X-ray normal", Assert.Single(record.Addenda).Text);
    }

    [Fact]
    public async Task Addenda_need_a_treating_relationship_and_a_closed_visit_day()
    {
        var w = new World();
        var past = w.Visit(w.Patient, w.Doctor, Yesterday);
        var same = w.Visit(w.Patient, w.Doctor, Today);
        var pastRecord = (await w.Service.CreateOrUpdateForVisitAsync(past.Id, w.Doctor.ApplicationUserId, Visit("Cough", "Flu"), CancellationToken.None)).RecordId!.Value;
        var sameRecord = (await w.Service.CreateOrUpdateForVisitAsync(same.Id, w.Doctor.ApplicationUserId, Visit("Cough", "Flu"), CancellationToken.None)).RecordId!.Value;

        var sameDay = await w.Service.AddAddendumAsync(sameRecord, w.Doctor.ApplicationUserId, "Too early", CancellationToken.None);
        var unrelated = await w.Service.AddAddendumAsync(pastRecord, w.OtherDoctor.ApplicationUserId, "Not mine", CancellationToken.None);

        Assert.False(sameDay.Succeeded);
        Assert.False(unrelated.Succeeded);
        Assert.Empty(w.Records.Records[pastRecord].Addenda);
    }

    [Fact]
    public async Task A_treating_doctor_outside_the_visit_can_add_an_addendum_after_the_visit_day()
    {
        var w = new World();
        // The other doctor has a treating relationship with the patient through a completed appointment.
        var appointment = Appointment.Request(w.Patient.Id, w.OtherDoctor.Id, Yesterday.AddDays(-7), new TimeOnly(9, 0), new TimeOnly(9, 20), At(Yesterday.AddDays(-8), 7, 0));
        appointment.Confirm(At(Yesterday.AddDays(-8), 7, 0));
        w.Appointments.Appointments[appointment.Id] = appointment;
        var entry = w.Visit(w.Patient, w.Doctor, Yesterday);
        var recordId = (await w.Service.CreateOrUpdateForVisitAsync(entry.Id, w.Doctor.ApplicationUserId, Visit("Cough", "Flu"), CancellationToken.None)).RecordId!.Value;

        var result = await w.Service.AddAddendumAsync(recordId, w.OtherDoctor.ApplicationUserId, "Second opinion", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Second opinion", Assert.Single(w.Records.Records[recordId].Addenda).Text);
    }

    [Fact]
    public async Task Prescription_needs_a_real_item_and_only_the_author_can_write_it_on_the_visit_day()
    {
        var w = new World();
        var entry = w.Visit(w.Patient, w.Doctor, Today);
        var recordId = (await w.Service.CreateOrUpdateForVisitAsync(entry.Id, w.Doctor.ApplicationUserId, Visit("Cough", "Flu"), CancellationToken.None)).RecordId!.Value;

        var blank = await w.Service.SavePrescriptionAsync(recordId, w.Doctor.ApplicationUserId, [Blank(), Blank()], null, CancellationToken.None);
        var stranger = await w.Service.SavePrescriptionAsync(recordId, w.OtherDoctor.ApplicationUserId, [Item("Amoxicillin")], null, CancellationToken.None);
        var saved = await w.Service.SavePrescriptionAsync(recordId, w.Doctor.ApplicationUserId, [Item("Amoxicillin"), Item("Paracetamol"), Blank()], "After food", CancellationToken.None);
        var replaced = await w.Service.SavePrescriptionAsync(recordId, w.Doctor.ApplicationUserId, [Item("Ibuprofen")], null, CancellationToken.None);

        Assert.False(blank.Succeeded);
        Assert.False(stranger.Succeeded);
        Assert.True(saved.Succeeded);
        Assert.True(replaced.Succeeded);
        var view = await w.Service.GetRecordAsync(recordId, w.Doctor.ApplicationUserId, MedicalViewerRole.Doctor, CancellationToken.None);
        Assert.Equal("Ibuprofen", Assert.Single(view!.Prescription!.Items).MedicationName);
    }

    [Fact]
    public async Task A_past_visit_prescription_cannot_be_written_or_changed()
    {
        var w = new World();
        var entry = w.Visit(w.Patient, w.Doctor, Yesterday);
        var recordId = (await w.Service.CreateOrUpdateForVisitAsync(entry.Id, w.Doctor.ApplicationUserId, Visit("Cough", "Flu"), CancellationToken.None)).RecordId!.Value;

        var result = await w.Service.SavePrescriptionAsync(recordId, w.Doctor.ApplicationUserId, [Item("Amoxicillin")], null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(w.Records.Records[recordId].Prescription);
    }

    [Fact]
    public async Task Record_reads_follow_the_policy_and_return_null_when_refused()
    {
        var w = new World();
        var entry = w.Visit(w.Patient, w.Doctor, Yesterday);
        var recordId = (await w.Service.CreateOrUpdateForVisitAsync(entry.Id, w.Doctor.ApplicationUserId, Visit("Cough", "Flu"), CancellationToken.None)).RecordId!.Value;

        Assert.NotNull(await w.Service.GetRecordAsync(recordId, w.Patient.ApplicationUserId, MedicalViewerRole.Patient, CancellationToken.None));
        Assert.NotNull(await w.Service.GetRecordAsync(recordId, w.Doctor.ApplicationUserId, MedicalViewerRole.Doctor, CancellationToken.None));
        Assert.Null(await w.Service.GetRecordAsync(recordId, w.OtherPatient.ApplicationUserId, MedicalViewerRole.Patient, CancellationToken.None));
        Assert.Null(await w.Service.GetRecordAsync(recordId, w.OtherDoctor.ApplicationUserId, MedicalViewerRole.Doctor, CancellationToken.None));
        Assert.Null(await w.Service.GetRecordAsync(recordId, Guid.NewGuid(), MedicalViewerRole.Receptionist, CancellationToken.None));
        Assert.Null(await w.Service.GetRecordAsync(recordId, Guid.NewGuid(), MedicalViewerRole.Admin, CancellationToken.None));
        // A patient role on an account that owns no patient profile reads nothing even if the role claim is present.
        Assert.Null(await w.Service.GetRecordAsync(recordId, Guid.NewGuid(), MedicalViewerRole.Patient, CancellationToken.None));
    }

    [Fact]
    public async Task Patient_history_is_newest_first_and_hidden_from_unrelated_viewers()
    {
        var w = new World();
        var older = w.Visit(w.Patient, w.Doctor, Today.AddDays(-3));
        var newer = w.Visit(w.Patient, w.Doctor, Yesterday);
        await w.Service.CreateOrUpdateForVisitAsync(older.Id, w.Doctor.ApplicationUserId, Visit("Cough", "Old"), CancellationToken.None);
        await w.Service.CreateOrUpdateForVisitAsync(newer.Id, w.Doctor.ApplicationUserId, Visit("Cough", "New"), CancellationToken.None);

        var mine = await w.Service.GetMyHistoryAsync(w.Patient.ApplicationUserId, CancellationToken.None);
        var byDoctor = await w.Service.GetPatientHistoryAsync(w.Patient.Id, w.Doctor.ApplicationUserId, MedicalViewerRole.Doctor, CancellationToken.None);
        var byUnrelated = await w.Service.GetPatientHistoryAsync(w.Patient.Id, w.OtherDoctor.ApplicationUserId, MedicalViewerRole.Doctor, CancellationToken.None);
        var byOtherPatient = await w.Service.GetPatientHistoryAsync(w.Patient.Id, w.OtherPatient.ApplicationUserId, MedicalViewerRole.Patient, CancellationToken.None);
        var unknownPatient = await w.Service.GetMyHistoryAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(mine);
        Assert.Equal(new[] { "New", "Old" }, mine!.Visits.Select(visit => visit.Diagnosis));
        Assert.Equal("Test Patient", mine.PatientName);
        Assert.Equal(2, byDoctor!.Visits.Count);
        Assert.Null(byUnrelated);
        Assert.Null(byOtherPatient);
        Assert.Null(unknownPatient);
    }

    [Fact]
    public async Task Active_prescriptions_list_only_unexpired_items_of_the_patient()
    {
        var w = new World();
        var current = w.Visit(w.Patient, w.Doctor, Yesterday);
        var currentRecord = await w.Service.CreateOrUpdateForVisitAsync(current.Id, w.Doctor.ApplicationUserId, Visit("Cough", "Flu"), CancellationToken.None);
        var expired = w.Visit(w.Patient, w.Doctor, Today.AddDays(-6));
        var expiredRecord = await w.Service.CreateOrUpdateForVisitAsync(expired.Id, w.Doctor.ApplicationUserId, Visit("Cough", "Old"), CancellationToken.None);
        var others = w.Visit(w.OtherPatient, w.Doctor, Yesterday);
        var otherRecord = await w.Service.CreateOrUpdateForVisitAsync(others.Id, w.Doctor.ApplicationUserId, Visit("Cough", "Other"), CancellationToken.None);

        // Prescriptions for past visits are written by the domain directly (the service only writes on the visit day).
        SeedPrescription(w, currentRecord.RecordId!.Value, Yesterday, ("Amoxicillin", 3), ("Paracetamol", 1));
        SeedPrescription(w, expiredRecord.RecordId!.Value, Today.AddDays(-6), ("Old drug", 2));
        SeedPrescription(w, otherRecord.RecordId!.Value, Yesterday, ("Other drug", 5));

        var active = await w.Service.GetActivePrescriptionsAsync(w.Patient.ApplicationUserId, CancellationToken.None);

        Assert.Equal(new[] { "Amoxicillin" }, active.Select(item => item.MedicationName));
        Assert.Equal(Yesterday.AddDays(2), active.Single().ActiveUntil);
    }

    [Fact]
    public async Task Doctor_visit_page_offers_create_or_edit_only_for_its_own_attempts()
    {
        var w = new World();
        var inConsultation = w.Visit(w.Patient, w.Doctor, Today);
        var waiting = w.Visit(w.Patient, w.Doctor, Today, inConsultation: false);

        var page = await w.Service.GetVisitForDoctorAsync(inConsultation.Id, w.Doctor.ApplicationUserId, CancellationToken.None);
        var strangers = await w.Service.GetVisitForDoctorAsync(inConsultation.Id, w.OtherDoctor.ApplicationUserId, CancellationToken.None);
        var notStarted = await w.Service.GetVisitForDoctorAsync(waiting.Id, w.Doctor.ApplicationUserId, CancellationToken.None);

        Assert.NotNull(page);
        Assert.True(page!.CanCreate);
        Assert.False(page.CanEdit);
        Assert.Null(strangers);
        Assert.False(notStarted!.CanCreate);
    }

    [Fact]
    public async Task Doctor_visit_list_contains_only_the_doctors_in_consultation_or_completed_attempts()
    {
        var w = new World();
        var started = w.Visit(w.Patient, w.Doctor, Today);
        var done = w.Visit(w.Patient, w.Doctor, Today, completed: true);
        w.Visit(w.Patient, w.Doctor, Today, inConsultation: false);
        w.Visit(w.Patient, w.OtherDoctor, Today);

        var list = await w.Service.GetDoctorVisitsForDateAsync(w.Doctor.ApplicationUserId, Today, CancellationToken.None);

        Assert.Equal(2, list.Count);
        Assert.Contains(list, item => item.QueueEntryId == started.Id && item.Status == QueueEntryStatus.InConsultation);
        Assert.Contains(list, item => item.QueueEntryId == done.Id && item.Status == QueueEntryStatus.Completed);
    }

    private static void SeedPrescription(World w, Guid recordId, DateOnly visitDate, params (string Name, int Days)[] items)
    {
        var record = w.Records.Records[recordId];
        var drafts = items.Select(item => new PrescriptionItemDraft(item.Name, "1 tab", "Once daily", item.Days, null)).ToArray();
        record.SetPrescription(record.DoctorId, visitDate, drafts, null, At(visitDate, 9, 30));
    }

    private static VisitRecordInput Visit(string complaint, string diagnosis) => new(complaint, diagnosis, null);

    private static PrescriptionItemInput Item(string name) => new(name, "500 mg", "Twice daily", 5, null);

    private static PrescriptionItemInput Blank() => new(null, "", "  ", null, null);

    private static DateTimeOffset At(DateOnly date, int hour, int minute) => new(date.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    private sealed class World
    {
        public World()
        {
            Clock = new FixedClock(At(Today, 10, 5), Today);
            Doctor = NewDoctor("Dr. Mona");
            OtherDoctor = NewDoctor("Dr. Karim");
            Appointments.AddDoctor(Doctor);
            Appointments.AddDoctor(OtherDoctor);
            Patient = QueueTestData.NewPatient("Test Patient");
            OtherPatient = QueueTestData.NewPatient("Other Patient");
            Records = new InMemoryMedicalRecordRepository(Appointments, Queue);
            Records.AddPatient(Patient);
            Records.AddPatient(OtherPatient);
            Service = new MedicalRecordService(Records, Appointments, UnitOfWork, Clock);
        }

        public FixedClock Clock { get; }
        public Doctor Doctor { get; }
        public Doctor OtherDoctor { get; }
        public Patient Patient { get; }
        public Patient OtherPatient { get; }
        public InMemoryAppointmentRepository Appointments { get; } = new();
        public InMemoryQueueRepository Queue { get; } = new();
        public InMemoryUnitOfWork UnitOfWork { get; } = new();
        public InMemoryMedicalRecordRepository Records { get; }
        public MedicalRecordService Service { get; }

        /// <summary>A scheduled attendance attempt on the date, optionally already in consultation or completed.</summary>
        public QueueEntry Visit(Patient patient, Doctor doctor, DateOnly date, bool inConsultation = true, bool completed = false)
        {
            var appointment = Appointment.Request(patient.Id, doctor.Id, date, new TimeOnly(9, 0), new TimeOnly(9, 20), At(date, 7, 0));
            appointment.Confirm(At(date, 7, 0));
            Appointments.Appointments[appointment.Id] = appointment;
            var arrived = At(date, 8, 55);
            var entry = QueueEntry.CreateScheduled(patient.Id, doctor.Id, appointment.Id, date, arrived);
            QueueTestData.Link(entry, patient, appointment);
            Queue.Add(entry);
            if (inConsultation || completed)
            {
                entry.Call(arrived.AddMinutes(5));
                entry.StartConsultation(arrived.AddMinutes(10));
            }
            if (completed)
            {
                entry.Complete(arrived.AddMinutes(20));
                appointment.MarkCompleted(arrived.AddMinutes(20));
            }
            return entry;
        }

        private static Doctor NewDoctor(string name)
        {
            var doctor = QueueTestData.NewDoctor();
            MedicalNav.Set(doctor, nameof(Doctor.Specialization), new Specialization(Guid.NewGuid(), "Internal medicine"));
            return doctor;
        }
    }
}
