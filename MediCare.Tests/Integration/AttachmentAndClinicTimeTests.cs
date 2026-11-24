using System.Globalization;
using System.Net;
using System.Text;
using MediCare.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Tests.Integration;

[Collection(PageWorldCollection.Name)]
public sealed class AttachmentAndClinicTimeTests(PageWorldFixture world)
{
    private static readonly byte[] SmallPdf = Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");

    [Fact]
    public async Task Doctor_uploads_a_pdf_that_the_doctor_and_the_patient_can_download_but_another_patient_cannot()
    {
        var doctor = await world.Data.CreateDoctorAsync();
        var patient = await world.Data.CreatePatientAsync();
        var stranger = await world.Data.CreatePatientAsync();
        var entry = await world.Data.CreateInConsultationEntryAsync(patient.PatientId, doctor.DoctorId, new TimeOnly(9, 0));
        var visitPath = $"/Doctor/Visit/{entry}";
        var doctorSession = await PageSession.SignInAsync(world.Factory, doctor.UserName, doctor.Password);

        var saved = await doctorSession.PostFormAsync(
            $"{visitPath}/Save",
            visitPath,
            [new("Visit.Diagnosis", "Check-up with attached report")]);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var recordId = await world.Data.QueryAsync(context =>
            context.MedicalRecords.Where(record => record.QueueEntryId == entry).Select(record => record.Id).SingleAsync());

        var upload = await doctorSession.PostMultipartAsync(
            $"/Doctor/Record/{recordId}/Attachments",
            visitPath,
            [new("description", "Lab report"), new("queueEntryId", entry.ToString()), new("patientId", patient.PatientId.ToString())],
            "file",
            "lab-report.pdf",
            "application/pdf",
            SmallPdf);
        Assert.Equal(HttpStatusCode.Redirect, upload.StatusCode);

        var attachmentId = await world.Data.QueryAsync(context =>
            context.MedicalAttachments.Where(attachment => attachment.MedicalRecordId == recordId).Select(attachment => attachment.Id).SingleAsync());

        var download = await doctorSession.GetAsync($"/Medical/Attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.StartsWith("attachment", ContentDisposition(download), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("nosniff", Header(download, "X-Content-Type-Options"));
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(await download.Content.ReadAsByteArrayAsync()), StringComparison.Ordinal);

        var patientSession = await PageSession.SignInAsync(world.Factory, patient.PhoneNumber, patient.Password);
        var ownerDownload = await patientSession.GetAsync($"/Medical/Attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.OK, ownerDownload.StatusCode);

        var strangerSession = await PageSession.SignInAsync(world.Factory, stranger.PhoneNumber, stranger.Password);
        var refused = await strangerSession.GetAsync($"/Medical/Attachments/{attachmentId}");
        Assert.NotEqual(HttpStatusCode.OK, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task Stored_utc_instants_are_shown_in_clinic_local_time()
    {
        // Regression: times on pages were shown in UTC. A June instant is +03:00 in Africa/Cairo.
        var patient = await world.Data.CreatePatientAsync();
        var instant = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        await world.Data.AddNotificationAsync(patient.UserId, "Clinic time check", instant);

        var zone = ClinicTimeZoneResolver.Resolve(MediCareWebApplicationFactory.ClinicTimeZoneId);
        var expectedLocal = TimeZoneInfo.ConvertTime(instant, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        var session = await PageSession.SignInAsync(world.Factory, patient.PhoneNumber, patient.Password);
        var html = await session.GetOkHtmlAsync("/Notifications");
        Assert.Contains(expectedLocal, html, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-06-01 10:00", html, StringComparison.Ordinal);
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(",", values)
            : string.Empty;

    private static string ContentDisposition(HttpResponseMessage response) =>
        response.Content.Headers.ContentDisposition?.ToString() ?? Header(response, "Content-Disposition");
}
