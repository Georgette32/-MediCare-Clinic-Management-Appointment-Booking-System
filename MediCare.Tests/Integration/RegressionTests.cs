using System.Net;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Tests.Integration;

/// <summary>
/// Bugs that were found only in a browser and are reproduced here through HTTP against the real application.
/// </summary>
[Collection(PageWorldCollection.Name)]
public sealed class RegressionTests(PageWorldFixture world)
{
    [Fact]
    public async Task Doctor_can_save_a_visit_and_then_resave_a_changed_prescription_without_server_errors()
    {
        // Regression: a prescription save returned 500 from EF Core. Saving the same prescription twice exercises the update path.
        var doctor = await world.Data.CreateDoctorAsync();
        var patient = await world.Data.CreatePatientAsync();
        var entry = await world.Data.CreateInConsultationEntryAsync(patient.PatientId, doctor.DoctorId, new TimeOnly(9, 0));
        var visitPath = $"/Doctor/Visit/{entry}";
        var session = await PageSession.SignInAsync(world.Factory, doctor.UserName, doctor.Password);

        var saveVisit = await session.PostFormAsync(
            $"{visitPath}/Save",
            visitPath,
            [new("Visit.ChiefComplaint", "Cough for three days"), new("Visit.Diagnosis", "Acute bronchitis"), new("Visit.ClinicalNotes", "Chest clear.")]);
        Assert.Equal(HttpStatusCode.Redirect, saveVisit.StatusCode);
        Assert.Equal(visitPath, PageSession.LocationPath(saveVisit));

        var recordId = await world.Data.QueryAsync(context =>
            context.MedicalRecords.Where(record => record.QueueEntryId == entry).Select(record => record.Id).SingleAsync());

        var firstSave = await session.PostFormAsync(
            $"/Doctor/Record/{recordId}/Prescription",
            visitPath,
            [
                new("Prescription.QueueEntryId", entry.ToString()),
                new("Prescription.Notes", "First version"),
                new("Prescription.Items[0].MedicationName", "Amoxicillin"),
                new("Prescription.Items[0].Dosage", "500 mg"),
                new("Prescription.Items[0].Frequency", "Three times daily"),
                new("Prescription.Items[0].DurationDays", "5"),
                new("Prescription.Items[0].Instructions", "After food"),
                new("Prescription.Items[1].MedicationName", ""),
                new("Prescription.Items[1].DurationDays", "")
            ]);
        Assert.Equal(HttpStatusCode.Redirect, firstSave.StatusCode);

        // The second save changes the values and the number of rows: the stored prescription must hold exactly the new items.
        var secondSave = await session.PostFormAsync(
            $"/Doctor/Record/{recordId}/Prescription",
            visitPath,
            [
                new("Prescription.QueueEntryId", entry.ToString()),
                new("Prescription.Notes", "Second version"),
                new("Prescription.Items[0].MedicationName", "Ibuprofen"),
                new("Prescription.Items[0].Dosage", "400 mg"),
                new("Prescription.Items[0].Frequency", "Twice daily"),
                new("Prescription.Items[0].DurationDays", "3"),
                new("Prescription.Items[1].MedicationName", "Paracetamol"),
                new("Prescription.Items[1].Dosage", "1 g"),
                new("Prescription.Items[1].Frequency", "When needed"),
                new("Prescription.Items[1].DurationDays", "2")
            ]);
        Assert.Equal(HttpStatusCode.Redirect, secondSave.StatusCode);

        var prescriptions = await world.Data.QueryAsync(context =>
            context.Prescriptions.Include(prescription => prescription.Items).Where(prescription => prescription.MedicalRecordId == recordId).ToListAsync());
        var prescription = Assert.Single(prescriptions);
        Assert.Equal("Second version", prescription.Notes);
        var items = prescription.Items.OrderBy(item => item.MedicationName).ToList();
        Assert.Equal(["Ibuprofen", "Paracetamol"], items.Select(item => item.MedicationName));
        Assert.Equal(3, items[0].DurationDays);
        Assert.Equal("400 mg", items[0].Dosage);

        var page = await session.GetOkHtmlAsync(visitPath);
        Assert.Contains("Ibuprofen", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Amoxicillin", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Doctor_statistics_and_admin_doctor_analytics_render()
    {
        // Regression: Doctor/Statistics returned 500 because a shared view's partials did not resolve.
        var doctor = await PageSession.SignInAsync(world.Factory, world.Doctor.UserName, world.Doctor.Password);
        var from = world.Data.ClinicToday.AddDays(-7).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var to = world.Data.ClinicToday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var statistics = await doctor.GetOkHtmlAsync($"/Doctor/Statistics?from={from}&to={to}");
        Assert.Contains("Statistics", statistics, StringComparison.OrdinalIgnoreCase);

        var admin = await PageSession.SignInAsync(world.Factory, world.Admin.UserName, world.Admin.Password);
        await admin.GetOkHtmlAsync($"/Analytics/Doctor/{world.Doctor.DoctorId}?from={from}&to={to}");
    }

    [Fact]
    public async Task Self_registered_patient_can_sign_in_with_the_local_phone_format()
    {
        // Regression: sign-in must accept 01... as well as the canonical +20... form.
        var (local, _) = PageTestData.NewEgyptianPhone();
        var email = $"local-{Guid.NewGuid():N}@example.invalid";
        var session = PageSession.Anonymous(world.Factory);
        var register = await session.PostFormAsync(
            "/Account/Register",
            "/Account/Register",
            [
                new("PhoneNumber", local),
                new("FullName", "Registered With Email"),
                new("Email", email),
                new("Password", PageTestData.UserPassword),
                new("ConfirmPassword", PageTestData.UserPassword)
            ]);
        Assert.Equal(HttpStatusCode.Redirect, register.StatusCode);
        Assert.Equal("/Account/VerifyEmail", PageSession.LocationPath(register));

        // A fresh sign-in with the 01... form works; the unverified account is then held on the verify page.
        var signedIn = await PageSession.SignInAsync(world.Factory, local, PageTestData.UserPassword);
        var held = await signedIn.GetAsync("/Appointments/Mine");
        Assert.Equal(HttpStatusCode.Redirect, held.StatusCode);
        Assert.Equal("/Account/VerifyEmail", PageSession.LocationPath(held));
    }

    [Fact]
    public async Task Patient_cannot_open_another_patients_reschedule_page_or_a_doctor_page()
    {
        var owner = await PageSession.SignInAsync(world.Factory, world.PatientWithAppointment.PhoneNumber, world.PatientWithAppointment.Password);
        var ownerPage = await owner.GetAsync($"/Appointments/Reschedule/{world.AppointmentOfPatientA}");
        Assert.Equal(HttpStatusCode.OK, ownerPage.StatusCode);

        var other = await PageSession.SignInAsync(world.Factory, world.PatientWithoutAppointments.PhoneNumber, world.PatientWithoutAppointments.Password);
        var foreignPage = await other.GetAsync($"/Appointments/Reschedule/{world.AppointmentOfPatientA}");
        Assert.Equal(HttpStatusCode.NotFound, foreignPage.StatusCode);

        var doctorPage = await other.GetAsync("/Doctor/Queue");
        Assert.Equal(HttpStatusCode.Redirect, doctorPage.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", PageSession.LocationPath(doctorPage), StringComparison.Ordinal);
    }
}
