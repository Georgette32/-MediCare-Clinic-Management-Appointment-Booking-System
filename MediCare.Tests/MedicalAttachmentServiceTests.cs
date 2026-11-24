using System.Security.Cryptography;
using MediCare.Application.Common;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.Extensions.Options;

namespace MediCare.Tests;

/// <summary>Upload and download of medical attachments: magic-byte validation, compensation, and the access matrix.</summary>
public sealed class MedicalAttachmentServiceTests
{
    private static readonly DateOnly Today = QueueTestData.Today;
    private const long MaxBytes = 4096;

    [Fact]
    public async Task A_treating_doctor_uploads_a_real_pdf_and_only_metadata_is_recorded()
    {
        var w = new World();
        var bytes = FileBytes.Pdf(500);

        var result = await w.Service.UploadAsync(w.Record.Id, w.Doctor.ApplicationUserId, Upload(bytes, "lab result.pdf", "Blood panel"), CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        var attachment = Assert.Single(w.Records.Records.Values.Single().Attachments);
        Assert.Equal(MedicalAttachment.ContentTypePdf, attachment.ContentType);
        Assert.Equal("lab result.pdf", attachment.OriginalFileName);
        Assert.Equal(bytes.Length, attachment.SizeBytes);
        Assert.Equal(FileBytes.Sha256Hex(bytes), attachment.Sha256);
        Assert.Equal("Blood panel", attachment.Description);
        Assert.Equal(attachment.StoredFileName, Assert.Single(w.Store.Files).Key);
        Assert.True(MedicalAttachment.IsValidStoredFileName(attachment.StoredFileName));
        Assert.DoesNotContain("lab", attachment.StoredFileName);
    }

    [Fact]
    public async Task The_declared_name_and_type_do_not_matter_only_the_bytes_do()
    {
        var w = new World();

        var jpeg = await w.Service.UploadAsync(w.Record.Id, w.Doctor.ApplicationUserId, Upload(FileBytes.Jpeg(), "scan.exe", null), CancellationToken.None);

        Assert.True(jpeg.Succeeded);
        Assert.Equal(MedicalAttachment.ContentTypeJpeg, Assert.Single(w.Records.Records.Values.Single().Attachments).ContentType);
        Assert.EndsWith(".jpg", Assert.Single(w.Store.Files).Key);
    }

    [Fact]
    public async Task An_executable_renamed_to_pdf_is_refused_and_nothing_is_written()
    {
        var w = new World();

        var result = await w.Service.UploadAsync(w.Record.Id, w.Doctor.ApplicationUserId, Upload(FileBytes.Executable(), "invoice.pdf", null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(w.Store.Files);
        Assert.Empty(w.Records.Records.Values.Single().Attachments);
    }

    [Fact]
    public async Task Empty_and_oversized_uploads_are_refused_and_nothing_is_written()
    {
        var w = new World();

        var empty = await w.Service.UploadAsync(w.Record.Id, w.Doctor.ApplicationUserId, Upload(Array.Empty<byte>(), "a.pdf", null), CancellationToken.None);
        var oversized = await w.Service.UploadAsync(w.Record.Id, w.Doctor.ApplicationUserId, Upload(FileBytes.Pdf((int)MaxBytes + 1), "big.pdf", null), CancellationToken.None);

        Assert.False(empty.Succeeded);
        Assert.False(oversized.Succeeded);
        Assert.Contains("larger than", oversized.Message);
        Assert.Empty(w.Store.Files);
    }

    [Fact]
    public async Task A_file_exactly_at_the_limit_is_accepted()
    {
        var w = new World();

        var result = await w.Service.UploadAsync(w.Record.Id, w.Doctor.ApplicationUserId, Upload(FileBytes.Png((int)MaxBytes), "edge.png", null), CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
    }

    [Fact]
    public async Task A_doctor_without_a_treating_relationship_cannot_upload_and_nothing_is_written()
    {
        var w = new World();

        var result = await w.Service.UploadAsync(w.Record.Id, w.OtherDoctor.ApplicationUserId, Upload(FileBytes.Pdf(), "x.pdf", null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(w.Store.Files);
    }

    [Fact]
    public async Task A_failed_database_write_after_the_file_was_written_deletes_the_file()
    {
        var w = new World();
        var service = new MedicalAttachmentService(w.Records, w.Attachments, w.Appointments, w.Store,
            new ThrowingUnitOfWork(new IOException("The connection was lost.")), w.Clock, w.Options);

        await Assert.ThrowsAsync<IOException>(() =>
            service.UploadAsync(w.Record.Id, w.Doctor.ApplicationUserId, Upload(FileBytes.Pdf(), "x.pdf", null), CancellationToken.None));

        Assert.Empty(w.Store.Files);
        Assert.Empty(w.Records.Records.Values.Single().Attachments);
    }

    [Fact]
    public async Task A_refused_database_write_also_removes_the_written_file()
    {
        var w = new World();
        var service = new MedicalAttachmentService(w.Records, w.Attachments, w.Appointments, w.Store,
            new ThrowingUnitOfWork(new InvalidOperationException("The record is locked.")), w.Clock, w.Options);

        var result = await service.UploadAsync(w.Record.Id, w.Doctor.ApplicationUserId, Upload(FileBytes.Pdf(), "x.pdf", null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(w.Store.Files);
    }

    [Fact]
    public async Task A_crafted_original_name_is_kept_for_display_only_and_never_used_on_disk()
    {
        var w = new World();

        var result = await w.Service.UploadAsync(w.Record.Id, w.Doctor.ApplicationUserId, Upload(FileBytes.Pdf(), "..\\..\\Windows\\system32\\evil.pdf", null), CancellationToken.None);

        Assert.True(result.Succeeded);
        var attachment = Assert.Single(w.Records.Records.Values.Single().Attachments);
        Assert.Equal("evil.pdf", attachment.OriginalFileName);
        Assert.Equal(attachment.StoredFileName, Assert.Single(w.Store.Files).Key);
        Assert.DoesNotContain("evil", attachment.StoredFileName);
    }

    [Fact]
    public async Task Over_long_descriptions_are_refused_before_anything_is_written()
    {
        var w = new World();

        var result = await w.Service.UploadAsync(w.Record.Id, w.Doctor.ApplicationUserId, Upload(FileBytes.Pdf(), "x.pdf", new string('d', 501)), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(w.Store.Files);
    }

    [Fact]
    public async Task The_treating_doctor_and_the_owning_patient_can_download_and_receive_the_same_bytes()
    {
        var w = new World();
        var bytes = FileBytes.Pdf(300);
        var uploaded = await w.Service.UploadAsync(w.Record.Id, w.Doctor.ApplicationUserId, Upload(bytes, "lab.pdf", null), CancellationToken.None);
        var attachmentId = uploaded.RecordId!.Value;

        var forDoctor = await w.Service.GetForDownloadAsync(attachmentId, w.Doctor.ApplicationUserId, MedicalViewerRole.Doctor, CancellationToken.None);
        var forPatient = await w.Service.GetForDownloadAsync(attachmentId, w.Patient.ApplicationUserId, MedicalViewerRole.Patient, CancellationToken.None);

        Assert.NotNull(forDoctor);
        Assert.NotNull(forPatient);
        Assert.Equal("lab.pdf", forDoctor!.FileName);
        Assert.Equal(MedicalAttachment.ContentTypePdf, forDoctor.ContentType);
        Assert.Equal(bytes, await ReadAllAsync(forDoctor.Content));
        Assert.Equal(bytes, await ReadAllAsync(forPatient!.Content));
    }

    [Fact]
    public async Task Download_is_refused_to_another_patient_to_a_doctor_without_the_relationship_and_to_reception_or_admin()
    {
        var w = new World();
        var uploaded = await w.Service.UploadAsync(w.Record.Id, w.Doctor.ApplicationUserId, Upload(FileBytes.Pdf(), "lab.pdf", null), CancellationToken.None);
        var attachmentId = uploaded.RecordId!.Value;

        var otherPatient = await w.Service.GetForDownloadAsync(attachmentId, w.OtherPatient.ApplicationUserId, MedicalViewerRole.Patient, CancellationToken.None);
        var otherDoctor = await w.Service.GetForDownloadAsync(attachmentId, w.OtherDoctor.ApplicationUserId, MedicalViewerRole.Doctor, CancellationToken.None);
        var reception = await w.Service.GetForDownloadAsync(attachmentId, Guid.NewGuid(), MedicalViewerRole.Receptionist, CancellationToken.None);
        var admin = await w.Service.GetForDownloadAsync(attachmentId, Guid.NewGuid(), MedicalViewerRole.Admin, CancellationToken.None);
        var unknown = await w.Service.GetForDownloadAsync(Guid.NewGuid(), w.Doctor.ApplicationUserId, MedicalViewerRole.Doctor, CancellationToken.None);

        Assert.Null(otherPatient);
        Assert.Null(otherDoctor);
        Assert.Null(reception);
        Assert.Null(admin);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task Uploaded_attachments_are_recorded_against_the_uploading_doctor()
    {
        var w = new World();
        await w.Service.UploadAsync(w.Record.Id, w.Doctor.ApplicationUserId, Upload(FileBytes.Png(), "scan.png", "Chest"), CancellationToken.None);

        var record = w.Records.Records.Values.Single();
        var attachment = Assert.Single(record.Attachments);
        Assert.Equal(w.Doctor.Id, attachment.UploadedByDoctorId);
        Assert.Equal(record.Id, attachment.MedicalRecordId);
    }

    private static MedicalFileUpload Upload(byte[] bytes, string fileName, string? description) =>
        new(new MemoryStream(bytes), fileName, description);

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    private sealed class World
    {
        public World()
        {
            Clock = new FixedClock(At(Today, 10, 5), Today);
            Doctor = QueueTestData.NewDoctor();
            OtherDoctor = QueueTestData.NewDoctor();
            Appointments.AddDoctor(Doctor);
            Appointments.AddDoctor(OtherDoctor);
            Patient = QueueTestData.NewPatient("Test Patient");
            OtherPatient = QueueTestData.NewPatient("Other Patient");
            Records = new InMemoryMedicalRecordRepository(Appointments, Queue);
            Records.AddPatient(Patient);
            Records.AddPatient(OtherPatient);
            Attachments = new InMemoryMedicalAttachmentRepository(Records);
            Options = Microsoft.Extensions.Options.Options.Create(new MedicalFileOptions { StoragePath = "App_Data/medical-files", MaxFileSizeBytes = MaxBytes });
            Service = new MedicalAttachmentService(Records, Attachments, Appointments, Store, UnitOfWork, Clock, Options);
            Record = VisitRecord();
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
        public InMemoryMedicalAttachmentRepository Attachments { get; }
        public InMemoryMedicalFileStore Store { get; } = new();
        public IOptions<MedicalFileOptions> Options { get; }
        public MedicalAttachmentService Service { get; }
        public MedicalRecord Record { get; }

        private MedicalRecord VisitRecord()
        {
            var appointment = Appointment.Request(Patient.Id, Doctor.Id, Today, new TimeOnly(9, 0), new TimeOnly(9, 20), At(Today, 7, 0));
            appointment.Confirm(At(Today, 7, 0));
            Appointments.Appointments[appointment.Id] = appointment;
            var entry = QueueEntry.CreateScheduled(Patient.Id, Doctor.Id, appointment.Id, Today, At(Today, 8, 55));
            QueueTestData.Link(entry, Patient, appointment);
            Queue.Add(entry);
            entry.Call(At(Today, 9, 0));
            entry.StartConsultation(At(Today, 9, 5));
            var record = MedicalRecord.CreateForVisit(entry, Doctor.Id, "Cough", "Bronchitis", null, At(Today, 9, 10));
            Records.AddRecordAsync(record, CancellationToken.None).GetAwaiter().GetResult();
            return record;
        }

        private static DateTimeOffset At(DateOnly date, int hour, int minute) => new(date.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);
    }
}
