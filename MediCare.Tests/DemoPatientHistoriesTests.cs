using MediCare.Domain.Entities;
using MediCare.Infrastructure.DemoData;

namespace MediCare.Tests;

/// <summary>The demo patient files are drafted outside the code, so every case is pushed through the real domain rules.</summary>
public sealed class DemoPatientHistoriesTests
{
    private static readonly DateOnly VisitDate = new(2026, 11, 1);
    private static readonly string[] Specializations = DemoDataCatalog.Doctors.Select(item => item.Specialization).Distinct().ToArray();

    [Fact]
    public void Every_demo_patient_has_a_profile_in_creation_order()
    {
        var patients = DemoPatientHistories.Patients;

        Assert.Equal(60, patients.Count);
        Assert.Equal(Enumerable.Range(0, patients.Count), patients.Select(item => item.Index));
        Assert.All(patients, patient =>
        {
            Assert.False(string.IsNullOrWhiteSpace(patient.Background));
            Assert.NotEmpty(patient.Conditions);
            Assert.Equal(patient.Conditions.Length, patient.Conditions.Select(item => item.Specialization).Distinct().Count());
        });
    }

    [Fact]
    public void Conditions_use_known_specializations_that_fit_the_patient_age()
    {
        foreach (var patient in DemoPatientHistories.Patients)
            foreach (var condition in patient.Conditions)
            {
                Assert.Contains(condition.Specialization, Specializations);
                Assert.True(condition.Visits.Length >= 3, $"Patient {patient.Index} {condition.Specialization} has too few visits.");
                if (patient.Child)
                    Assert.DoesNotContain(condition.Specialization, new[] { "Cardiology", "Internal Medicine" });
                else
                    Assert.NotEqual("Pediatrics", condition.Specialization);
            }

        Assert.Contains(DemoPatientHistories.Patients, item => item.Child);
    }

    [Fact]
    public void Every_specialization_has_an_acute_pool()
    {
        foreach (var specialization in Specializations)
            Assert.True(DemoPatientHistories.Acute.TryGetValue(specialization, out var cases) && cases.Length >= 5,
                $"{specialization} needs at least 5 acute cases.");
    }

    [Fact]
    public void Every_case_passes_the_medical_record_and_prescription_rules()
    {
        var cases = DemoPatientHistories.Patients
            .SelectMany(patient => patient.Conditions.SelectMany(condition => condition.Visits
                .Select(visit => (Where: $"patient {patient.Index} {condition.Specialization}", Case: visit,
                    Notes: $"Medical history (taken at the first visit): {patient.Background}\n\n{visit.ClinicalNotes}"))))
            .Concat(DemoPatientHistories.Acute.SelectMany(pool => pool.Value.Select(visit => (Where: $"acute {pool.Key}", Case: visit, Notes: visit.ClinicalNotes))));

        foreach (var (where, clinicalCase, notes) in cases)
        {
            var doctorId = Guid.NewGuid();
            var entry = CompletedEntry(doctorId);
            var record = Record(where, () => MedicalRecord.CreateForVisit(entry, doctorId, clinicalCase.ChiefComplaint, clinicalCase.Diagnosis, notes, Now));
            Assert.False(string.IsNullOrWhiteSpace(record.ChiefComplaint), $"{where}: chief complaint is empty.");
            if (clinicalCase.Medications.Length > 0)
                Record(where, () => record.SetPrescription(doctorId, VisitDate, clinicalCase.Medications, clinicalCase.PrescriptionNotes, Now));
        }
    }

    private static T Record<T>(string where, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (ArgumentException exception)
        {
            throw new Xunit.Sdk.XunitException($"{where}: {exception.Message}");
        }
    }

    private static DateTimeOffset Now => new(VisitDate.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);

    private static QueueEntry CompletedEntry(Guid doctorId)
    {
        var arrived = new DateTimeOffset(VisitDate.ToDateTime(new TimeOnly(8, 55)), TimeSpan.Zero);
        var entry = QueueEntry.CreateScheduled(Guid.NewGuid(), doctorId, Guid.NewGuid(), VisitDate, arrived);
        entry.Call(arrived.AddMinutes(5));
        entry.StartConsultation(arrived.AddMinutes(10));
        entry.Complete(arrived.AddMinutes(20));
        return entry;
    }
}
