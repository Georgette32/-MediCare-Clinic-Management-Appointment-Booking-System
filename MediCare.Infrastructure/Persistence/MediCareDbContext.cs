using MediCare.Domain.Entities;
using MediCare.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

public sealed class MediCareDbContext(DbContextOptions<MediCareDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<Specialization> Specializations => Set<Specialization>();
    public DbSet<DoctorWorkingHour> DoctorWorkingHours => Set<DoctorWorkingHour>();
    public DbSet<DoctorWorkingHourSnapshot> DoctorWorkingHourSnapshots => Set<DoctorWorkingHourSnapshot>();
    public DbSet<DoctorBreak> DoctorBreaks => Set<DoctorBreak>();
    public DbSet<DoctorHoliday> DoctorHolidays => Set<DoctorHoliday>();
    public DbSet<DoctorWorkingHourExtension> DoctorWorkingHourExtensions => Set<DoctorWorkingHourExtension>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
    public DbSet<WalkInAdmissionApproval> WalkInAdmissionApprovals => Set<WalkInAdmissionApproval>();
    public DbSet<DoctorDisruption> DoctorDisruptions => Set<DoctorDisruption>();
    public DbSet<DisruptionWorklistItem> DisruptionWorklistItems => Set<DisruptionWorklistItem>();
    public DbSet<DisruptionContactAttempt> DisruptionContactAttempts => Set<DisruptionContactAttempt>();
    public DbSet<ClinicSettings> ClinicSettings => Set<ClinicSettings>();
    public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
    public DbSet<MedicalRecordAddendum> MedicalRecordAddenda => Set<MedicalRecordAddendum>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();
    public DbSet<MedicalAttachment> MedicalAttachments => Set<MedicalAttachment>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>().Property(item => item.FullName).HasMaxLength(200).IsRequired();
        // Email verification state. The migration adds the columns with false/0 defaults, so every account that existed
        // before this feature stays unrestricted.
        builder.Entity<ApplicationUser>().Property(item => item.EmailCodeHash).HasMaxLength(64);
        // Identity's EmailIndex, made unique for non-null emails only (email is optional for patients).
        builder.Entity<ApplicationUser>().HasIndex(item => item.NormalizedEmail).IsUnique().HasFilter("[NormalizedEmail] IS NOT NULL");

        builder.Entity<Patient>().HasIndex(item => item.PhoneNumber).IsUnique();

        builder.Entity<Patient>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.FullName).HasMaxLength(200).IsRequired();
            entity.Property(item => item.PhoneNumber).HasMaxLength(32).IsRequired();
            entity.HasIndex(item => item.ApplicationUserId).IsUnique();
            entity.HasOne<ApplicationUser>().WithOne().HasForeignKey<Patient>(item => item.ApplicationUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Doctor>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.FullName).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Biography).HasMaxLength(Doctor.MaxBiographyLength);
            entity.HasOne(item => item.Specialization).WithMany().HasForeignKey(item => item.SpecializationId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => item.ApplicationUserId).IsUnique();
            entity.HasOne<ApplicationUser>().WithOne().HasForeignKey<Doctor>(item => item.ApplicationUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Doctors_AppointmentDurationPositive", "[AppointmentDurationMinutes] > 0");
                table.HasCheckConstraint("CK_Doctors_NextDuration",
                    "([NextAppointmentDurationMinutes] IS NULL AND [NextDurationEffectiveFrom] IS NULL) OR ([NextAppointmentDurationMinutes] IS NOT NULL AND [NextDurationEffectiveFrom] IS NOT NULL AND [NextAppointmentDurationMinutes] > 0)");
            });
        });

        builder.Entity<Specialization>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(120).IsRequired();
            entity.HasIndex(item => item.Name).IsUnique();
        });

        builder.Entity<DoctorWorkingHour>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.DoctorId, item.DayOfWeek, item.StartTime }).IsUnique();
            entity.HasOne(item => item.Doctor).WithMany().HasForeignKey(item => item.DoctorId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint("CK_DoctorWorkingHours_TimeRange", "[EndTime] > [StartTime]"));
        });

        // Append-only history of working windows. Analytics reads the snapshot in force on a date, so a later schedule
        // change does not rewrite past open time. DoctorWorkingHours itself stays unchanged.
        builder.Entity<DoctorWorkingHourSnapshot>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.DoctorId, item.EffectiveFrom });
            entity.HasIndex(item => item.WorkingHourId);
            entity.HasOne(item => item.WorkingHour).WithMany().HasForeignKey(item => item.WorkingHourId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Doctor>().WithMany().HasForeignKey(item => item.DoctorId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<DoctorBreak>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.DoctorId, item.Date, item.StartTime });
            entity.HasIndex(item => item.DisruptionId).HasFilter("[DisruptionId] IS NOT NULL");
            entity.HasOne(item => item.Doctor).WithMany().HasForeignKey(item => item.DoctorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DoctorDisruption>().WithMany().HasForeignKey(item => item.DisruptionId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint("CK_DoctorBreaks_TimeRange", "[EndTime] > [StartTime]"));
        });

        builder.Entity<DoctorHoliday>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Reason).HasMaxLength(500);
            entity.HasIndex(item => new { item.DoctorId, item.StartDate, item.EndDate });
            entity.HasOne(item => item.Doctor).WithMany().HasForeignKey(item => item.DoctorId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint("CK_DoctorHolidays_DateRange", "[EndDate] >= [StartDate]"));
        });

        builder.Entity<DoctorWorkingHourExtension>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(item => item.Reason).HasMaxLength(DoctorWorkingHourExtension.MaxReasonLength).IsRequired();
            entity.Property(item => item.DecisionNotes).HasMaxLength(DoctorWorkingHourExtension.MaxDecisionNotesLength);
            // One live (Pending or Approved) extension per doctor window and date; rejected/cancelled rows are history.
            entity.HasIndex(item => new { item.DoctorId, item.Date, item.OriginalEndTime }).IsUnique()
                .HasFilter("[Status] IN ('Pending', 'Approved')");
            entity.HasIndex(item => new { item.DoctorId, item.Date });
            entity.HasOne(item => item.Doctor).WithMany().HasForeignKey(item => item.DoctorId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.RequestedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_DoctorWorkingHourExtensions_TimeRange", "[ExtendedEndTime] > [OriginalEndTime]");
                table.HasCheckConstraint("CK_DoctorWorkingHourExtensions_Status",
                    "[Status] IN ('Pending', 'Approved', 'Rejected', 'Cancelled')");
                table.HasCheckConstraint("CK_DoctorWorkingHourExtensions_DecisionTimestamp",
                    "([Status] = 'Pending' AND [DecidedAtUtc] IS NULL) OR " +
                    "([Status] IN ('Approved', 'Rejected', 'Cancelled') AND [DecidedAtUtc] IS NOT NULL)");
                table.HasCheckConstraint("CK_DoctorWorkingHourExtensions_DecidedAfterRequest",
                    "[DecidedAtUtc] IS NULL OR [DecidedAtUtc] >= [RequestedAtUtc]");
            });
        });

        builder.Entity<Appointment>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(item => item.CancellationReason).HasMaxLength(500);
            entity.HasIndex(item => new { item.DoctorId, item.AppointmentDate, item.StartTime, item.EndTime });
            entity.HasIndex(item => new { item.DoctorId, item.AppointmentDate, item.StartTime }).IsUnique()
                .HasFilter("[Status] IN ('Pending', 'Confirmed')");
            entity.HasIndex(item => new { item.PatientId, item.AppointmentDate });
            entity.HasIndex(item => item.RescheduledFromAppointmentId).IsUnique()
                .HasFilter("[RescheduledFromAppointmentId] IS NOT NULL AND [Status] IN ('Pending', 'Confirmed')");
            entity.HasOne(item => item.Patient).WithMany().HasForeignKey(item => item.PatientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Doctor).WithMany().HasForeignKey(item => item.DoctorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.RescheduledFromAppointment).WithMany().HasForeignKey(item => item.RescheduledFromAppointmentId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Appointments_TimeRange", "[EndTime] > [StartTime]");
                table.HasCheckConstraint("CK_Appointments_GracePeriodPositive", "[FrozenGracePeriodMinutes] IS NULL OR [FrozenGracePeriodMinutes] > 0");
            });
        });

        builder.Entity<QueueEntry>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Source).HasConversion<string>().HasMaxLength(16);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(item => item.Status).IsConcurrencyToken();
            // One LIVE attendance attempt per appointment; Cancelled, NoShow and Completed attempts are history.
            entity.HasIndex(item => item.AppointmentId).IsUnique()
                .HasFilter("[AppointmentId] IS NOT NULL AND [Status] IN ('Waiting', 'Called', 'InConsultation')");
            entity.HasIndex(item => new { item.DoctorId, item.QueueDate, item.QueueSequence }).IsUnique().HasFilter("[QueueSequence] IS NOT NULL");
            entity.HasIndex(item => item.DoctorId).IsUnique().HasFilter("[Status] = 'InConsultation'");
            entity.HasIndex(item => new { item.DoctorId, item.Source, item.QueueDate, item.ArrivedAt, item.QueueSequence });
            // Analytics: clinic-wide and doctor-set date-range reads seek on QueueDate and cover the timestamp projection.
            entity.HasIndex(item => new { item.QueueDate, item.DoctorId })
                .IncludeProperties(item => new { item.Status, item.Source, item.PatientId, item.AppointmentId, item.ArrivedAt, item.CalledAt, item.ConsultationStartedAt, item.CompletedAt });
            entity.HasOne(item => item.Patient).WithMany().HasForeignKey(item => item.PatientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Doctor).WithMany().HasForeignKey(item => item.DoctorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Appointment).WithMany().HasForeignKey(item => item.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_QueueEntries_SourceAppointmentSequence",
                "([Source] = 'WalkIn' AND [AppointmentId] IS NULL AND [QueueSequence] IS NOT NULL AND [QueueSequence] > 0) OR ([Source] = 'Scheduled' AND [AppointmentId] IS NOT NULL AND [QueueSequence] IS NULL)"));
        });

        builder.Entity<WalkInAdmissionApproval>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Decision).HasConversion<string>().HasMaxLength(16).IsConcurrencyToken();
            entity.Property(item => item.UsedAtUtc).IsConcurrencyToken();
            entity.HasIndex(item => item.QueueEntryId).IsUnique().HasFilter("[Decision] = 'Pending'");
            entity.HasIndex(item => new { item.DoctorId, item.Decision, item.RequestedAtUtc });
            entity.HasOne(item => item.QueueEntry).WithMany().HasForeignKey(item => item.QueueEntryId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Doctor>().WithMany().HasForeignKey(item => item.DoctorId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_WalkInAdmissionApprovals_DecisionTimestamp",
                "[Decision] IN ('Pending', 'Approved', 'Rejected', 'Expired') AND " +
                "(([Decision] = 'Pending' AND [DecisionAtUtc] IS NULL AND [UsedAtUtc] IS NULL) OR " +
                "([Decision] = 'Approved' AND [DecisionAtUtc] IS NOT NULL) OR " +
                "([Decision] IN ('Rejected', 'Expired') AND [DecisionAtUtc] IS NOT NULL AND [UsedAtUtc] IS NULL)) AND " +
                "([UsedAtUtc] IS NULL OR [DecisionAtUtc] IS NULL OR [UsedAtUtc] >= [DecisionAtUtc])"));
        });

        builder.Entity<DoctorDisruption>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Reason).HasMaxLength(DoctorDisruption.MaxReasonLength).IsRequired();
            // One disruption per doctor and date; a second one for the same day is refused.
            entity.HasIndex(item => new { item.DoctorId, item.Date }).IsUnique();
            entity.HasIndex(item => item.RecordedAtUtc);
            entity.HasOne(item => item.Doctor).WithMany().HasForeignKey(item => item.DoctorId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.RecordedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.DayEndRecordedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_DoctorDisruptions_ReasonRequired", "LEN(LTRIM(RTRIM([Reason]))) > 0");
                table.HasCheckConstraint("CK_DoctorDisruptions_DayEndPair",
                    "([DayEndRecordedAtUtc] IS NULL AND [DayEndRecordedByUserId] IS NULL) OR " +
                    "([DayEndRecordedAtUtc] IS NOT NULL AND [DayEndRecordedByUserId] IS NOT NULL)");
                table.HasCheckConstraint("CK_DoctorDisruptions_DayEndAfterRecord",
                    "[DayEndRecordedAtUtc] IS NULL OR [DayEndRecordedAtUtc] >= [RecordedAtUtc]");
            });
        });

        builder.Entity<DisruptionWorklistItem>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Outcome).HasConversion<string>().HasMaxLength(16);
            entity.Property(item => item.Notes).HasMaxLength(DisruptionWorklistItem.MaxNotesLength);
            entity.Property(item => item.ResolutionNote).HasMaxLength(DisruptionWorklistItem.MaxResolutionNoteLength);
            // An appointment or a queue entry appears once per disruption.
            entity.HasIndex(item => new { item.DisruptionId, item.AppointmentId }).IsUnique().HasFilter("[AppointmentId] IS NOT NULL");
            entity.HasIndex(item => new { item.DisruptionId, item.QueueEntryId }).IsUnique().HasFilter("[QueueEntryId] IS NOT NULL");
            entity.HasIndex(item => item.AppointmentId).HasFilter("[AppointmentId] IS NOT NULL");
            entity.HasIndex(item => item.QueueEntryId).HasFilter("[QueueEntryId] IS NOT NULL");
            entity.HasOne(item => item.Disruption).WithMany().HasForeignKey(item => item.DisruptionId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Patient).WithMany().HasForeignKey(item => item.PatientId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Appointment).WithMany().HasForeignKey(item => item.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.QueueEntry).WithMany().HasForeignKey(item => item.QueueEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.LastContactedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_DisruptionWorklistItems_Link",
                    "[AppointmentId] IS NOT NULL OR [QueueEntryId] IS NOT NULL");
                table.HasCheckConstraint("CK_DisruptionWorklistItems_Outcome",
                    "[Outcome] IN ('Pending', 'Contacted', 'Rescheduled', 'Cancelled')");
                table.HasCheckConstraint("CK_DisruptionWorklistItems_ContactPair",
                    "([LastContactedByUserId] IS NULL AND [LastContactedAtUtc] IS NULL) OR " +
                    "([LastContactedByUserId] IS NOT NULL AND [LastContactedAtUtc] IS NOT NULL)");
                // A closed outcome is always resolved; an automatic closure without an outcome carries a note.
                table.HasCheckConstraint("CK_DisruptionWorklistItems_ClosedOutcomeResolved",
                    "[Outcome] NOT IN ('Rescheduled', 'Cancelled') OR [ResolvedAtUtc] IS NOT NULL");
                table.HasCheckConstraint("CK_DisruptionWorklistItems_ResolutionNote",
                    "[ResolvedAtUtc] IS NULL OR [Outcome] IN ('Rescheduled', 'Cancelled') OR [ResolutionNote] IS NOT NULL");
            });
        });

        builder.Entity<ClinicSettings>(entity =>
        {
            // Single-row table: the fixed Id is enforced by a check constraint, and nothing is seeded.
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedNever();
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_ClinicSettings_SingletonId", "[Id] = 1");
                table.HasCheckConstraint("CK_ClinicSettings_MinimumSampleSizePositive",
                    "[MinimumConsultationSampleSize] IS NULL OR [MinimumConsultationSampleSize] > 0");
            });
        });

        builder.Entity<MedicalRecord>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ChiefComplaint).HasMaxLength(MedicalRecord.MaxChiefComplaintLength);
            entity.Property(item => item.Diagnosis).HasMaxLength(MedicalRecord.MaxDiagnosisLength).IsRequired();
            entity.Property(item => item.ClinicalNotes).HasMaxLength(MedicalRecord.MaxClinicalNotesLength);
            // One record per attendance attempt.
            entity.HasIndex(item => item.QueueEntryId).IsUnique();
            entity.HasIndex(item => new { item.PatientId, item.VisitDate });
            entity.HasIndex(item => new { item.DoctorId, item.VisitDate });
            entity.HasIndex(item => item.AppointmentId).HasFilter("[AppointmentId] IS NOT NULL");
            entity.HasOne(item => item.Patient).WithMany().HasForeignKey(item => item.PatientId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Doctor).WithMany().HasForeignKey(item => item.DoctorId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<QueueEntry>().WithMany().HasForeignKey(item => item.QueueEntryId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Appointment>().WithMany().HasForeignKey(item => item.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Prescription).WithOne().HasForeignKey<Prescription>(item => item.MedicalRecordId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(item => item.Addenda).WithOne().HasForeignKey(item => item.MedicalRecordId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(item => item.Attachments).WithOne().HasForeignKey(item => item.MedicalRecordId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_MedicalRecords_DiagnosisRequired", "LEN(LTRIM(RTRIM([Diagnosis]))) > 0");
                table.HasCheckConstraint("CK_MedicalRecords_UpdatedAfterCreated",
                    "[UpdatedAtUtc] IS NULL OR [UpdatedAtUtc] >= [CreatedAtUtc]");
            });
        });

        builder.Entity<MedicalRecordAddendum>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Text).HasMaxLength(MedicalRecordAddendum.MaxTextLength).IsRequired();
            entity.HasIndex(item => new { item.MedicalRecordId, item.CreatedAtUtc });
            entity.HasOne(item => item.Doctor).WithMany().HasForeignKey(item => item.DoctorId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint("CK_MedicalRecordAddenda_TextRequired", "LEN(LTRIM(RTRIM([Text]))) > 0"));
        });

        builder.Entity<Prescription>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Notes).HasMaxLength(Prescription.MaxNotesLength);
            // One prescription per medical record.
            entity.HasIndex(item => item.MedicalRecordId).IsUnique();
            // Items are owned by their prescription and replaced as a whole on the visit day, so EF deletes the
            // replaced rows (ClientCascade). The database FK stays NO ACTION.
            entity.HasMany(item => item.Items).WithOne().HasForeignKey(item => item.PrescriptionId).IsRequired().OnDelete(DeleteBehavior.ClientCascade);
            entity.ToTable(table => table.HasCheckConstraint("CK_Prescriptions_NotesNotBlank",
                "[Notes] IS NULL OR LEN(LTRIM(RTRIM([Notes]))) > 0"));
        });

        builder.Entity<PrescriptionItem>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.MedicationName).HasMaxLength(PrescriptionItem.MaxMedicationNameLength).IsRequired();
            entity.Property(item => item.Dosage).HasMaxLength(PrescriptionItem.MaxDosageLength).IsRequired();
            entity.Property(item => item.Frequency).HasMaxLength(PrescriptionItem.MaxFrequencyLength).IsRequired();
            entity.Property(item => item.Instructions).HasMaxLength(PrescriptionItem.MaxInstructionsLength);
            entity.HasIndex(item => item.PrescriptionId);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_PrescriptionItems_DurationPositive", "[DurationDays] > 0");
                table.HasCheckConstraint("CK_PrescriptionItems_TextRequired",
                    "LEN(LTRIM(RTRIM([MedicationName]))) > 0 AND LEN(LTRIM(RTRIM([Dosage]))) > 0 AND LEN(LTRIM(RTRIM([Frequency]))) > 0");
            });
        });

        builder.Entity<MedicalAttachment>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.OriginalFileName).HasMaxLength(MedicalAttachment.MaxOriginalFileNameLength).IsRequired();
            entity.Property(item => item.ContentType).HasMaxLength(32).IsRequired();
            entity.Property(item => item.StoredFileName).HasMaxLength(MedicalAttachment.StoredFileNameLength).IsRequired();
            entity.Property(item => item.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(MedicalAttachment.MaxDescriptionLength);
            // Stored names are server-generated and never reused.
            entity.HasIndex(item => item.StoredFileName).IsUnique();
            entity.HasIndex(item => new { item.MedicalRecordId, item.UploadedAtUtc });
            entity.HasOne(item => item.UploadedByDoctor).WithMany().HasForeignKey(item => item.UploadedByDoctorId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_MedicalAttachments_ContentType", "[ContentType] IN ('application/pdf', 'image/jpeg', 'image/png')");
                table.HasCheckConstraint("CK_MedicalAttachments_SizePositive", "[SizeBytes] > 0");
                table.HasCheckConstraint("CK_MedicalAttachments_Sha256Length", "LEN([Sha256]) = 64");
                table.HasCheckConstraint("CK_MedicalAttachments_StoredFileNameLength", "LEN([StoredFileName]) = 36");
                table.HasCheckConstraint("CK_MedicalAttachments_OriginalFileNameRequired", "LEN(LTRIM(RTRIM([OriginalFileName]))) > 0");
                table.HasCheckConstraint("CK_MedicalAttachments_DescriptionNotBlank",
                    "[Description] IS NULL OR LEN(LTRIM(RTRIM([Description]))) > 0");
            });
        });

        builder.Entity<Notification>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Kind).HasMaxLength(Notification.MaxKindLength).IsRequired();
            entity.Property(item => item.Title).HasMaxLength(Notification.MaxTitleLength).IsRequired();
            entity.Property(item => item.Message).HasMaxLength(Notification.MaxMessageLength).IsRequired();
            entity.Property(item => item.RelatedEntityType).HasMaxLength(Notification.MaxRelatedEntityTypeLength);
            entity.Property(item => item.DedupKey).HasMaxLength(Notification.MaxDedupKeyLength);
            // At most one notification per recipient and dedup key (reminders and other idempotent deliveries).
            entity.HasIndex(item => new { item.RecipientUserId, item.DedupKey }).IsUnique().HasFilter("[DedupKey] IS NOT NULL");
            entity.HasIndex(item => new { item.RecipientUserId, item.ReadAtUtc, item.CreatedAtUtc });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.RecipientUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Notifications_KindRequired", "LEN(LTRIM(RTRIM([Kind]))) > 0");
                table.HasCheckConstraint("CK_Notifications_TitleRequired", "LEN(LTRIM(RTRIM([Title]))) > 0");
                table.HasCheckConstraint("CK_Notifications_MessageRequired", "LEN(LTRIM(RTRIM([Message]))) > 0");
                table.HasCheckConstraint("CK_Notifications_DedupKeyNotBlank", "[DedupKey] IS NULL OR LEN(LTRIM(RTRIM([DedupKey]))) > 0");
                table.HasCheckConstraint("CK_Notifications_RelatedPair",
                    "([RelatedEntityType] IS NULL AND [RelatedEntityId] IS NULL) OR ([RelatedEntityType] IS NOT NULL AND [RelatedEntityId] IS NOT NULL)");
            });
        });

        builder.Entity<DisruptionContactAttempt>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Outcome).HasConversion<string>().HasMaxLength(16);
            entity.Property(item => item.Notes).HasMaxLength(DisruptionWorklistItem.MaxNotesLength);
            entity.HasIndex(item => new { item.WorklistItemId, item.ContactedAtUtc });
            entity.HasOne<DisruptionWorklistItem>().WithMany().HasForeignKey(item => item.WorklistItemId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.ContactedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_DisruptionContactAttempts_Outcome",
                "[Outcome] IN ('Pending', 'Contacted', 'Rescheduled', 'Cancelled')"));
        });

        // Domain entities create their own Guid keys in their factories. Telling EF the keys are never
        // database-generated makes a new child reached through a navigation (a prescription, its items, an
        // addendum, an attachment) an INSERT instead of an UPDATE of a row that does not exist.
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (entityType.ClrType.Namespace != typeof(Patient).Namespace)
                continue;
            var key = entityType.FindPrimaryKey();
            if (key is { Properties.Count: 1 } && key.Properties[0].ClrType == typeof(Guid))
                builder.Entity(entityType.ClrType).Property(key.Properties[0].Name).ValueGeneratedNever();
        }
    }
}
