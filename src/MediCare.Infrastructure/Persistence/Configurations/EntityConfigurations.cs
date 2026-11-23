using MediCare.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Infrastructure.Persistence.Configurations;

internal sealed class SpecializationConfiguration : IEntityTypeConfiguration<Specialization>
{
    public void Configure(EntityTypeBuilder<Specialization> builder)
    {
        builder.ToTable("Specializations");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(120).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(500);
        builder.HasIndex(s => s.Name).IsUnique();
        builder.HasMany(s => s.Doctors)
            .WithOne(d => d.Specialization)
            .HasForeignKey(d => d.SpecializationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DoctorProfileConfiguration : IEntityTypeConfiguration<DoctorProfile>
{
    public void Configure(EntityTypeBuilder<DoctorProfile> builder)
    {
        builder.ToTable("DoctorProfiles", table =>
            table.HasCheckConstraint(
                "CK_DoctorProfiles_AppointmentDuration",
                "[AppointmentDurationMinutes] BETWEEN 5 AND 240"));
        builder.HasKey(d => d.Id);
        builder.Property(d => d.AppointmentDurationMinutes).HasDefaultValue(30);
        builder.Property(d => d.LicenseNumber).HasMaxLength(60).IsRequired();
        builder.Property(d => d.Bio).HasMaxLength(2000);
        builder.Property(d => d.ConsultationFee).HasPrecision(18, 2);
        builder.Property(d => d.ApprovalStatus).HasConversion<int>();
        builder.Property(d => d.RejectionReason).HasMaxLength(1000);
        builder.HasIndex(d => d.UserId).IsUnique();
        builder.HasIndex(d => d.LicenseNumber).IsUnique();
        builder.HasIndex(d => d.ApprovalStatus);
        builder.Ignore(d => d.IsApproved);
        builder.HasMany(d => d.Availabilities)
            .WithOne(a => a.DoctorProfile)
            .HasForeignKey(a => a.DoctorProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PatientProfileConfiguration : IEntityTypeConfiguration<PatientProfile>
{
    public void Configure(EntityTypeBuilder<PatientProfile> builder)
    {
        builder.ToTable("PatientProfiles");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Gender).HasConversion<int>();
        builder.Property(p => p.Address).HasMaxLength(300);
        builder.Property(p => p.EmergencyContact).HasMaxLength(150);
        builder.Property(p => p.BloodType).HasMaxLength(10);
        builder.HasIndex(p => p.UserId).IsUnique();
    }
}

internal sealed class DoctorAvailabilityConfiguration : IEntityTypeConfiguration<DoctorAvailability>
{
    public void Configure(EntityTypeBuilder<DoctorAvailability> builder)
    {
        builder.ToTable("DoctorAvailabilities");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.DayOfWeek).HasConversion<int>();
        builder.HasIndex(a => new { a.DoctorProfileId, a.DayOfWeek });
    }
}

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments", table =>
        {
            table.HasCheckConstraint("CK_Appointments_TimeRange", "[EndTime] > [StartTime]");
            table.HasCheckConstraint(
                "CK_Appointments_GracePeriodPositive",
                "[FrozenGracePeriodMinutes] IS NULL OR [FrozenGracePeriodMinutes] > 0");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Status).HasConversion<int>();
        builder.Property(a => a.Fee).HasPrecision(18, 2);
        builder.Property(a => a.Reason).HasMaxLength(1000);
        builder.Property(a => a.DoctorNotes).HasMaxLength(2000);
        builder.Property(a => a.CancellationReason).HasMaxLength(500);
        builder.Property(a => a.AppointmentDate).HasColumnType("date");
        builder.Property(a => a.StartTime).HasColumnType("time(0)");
        builder.Property(a => a.EndTime).HasColumnType("time(0)");

        builder.HasIndex(a => a.ScheduledAtUtc);
        builder.HasIndex(a => a.Status);
        builder.HasIndex(a => new
        {
            a.DoctorProfileId,
            a.AppointmentDate,
            a.StartTime,
            a.EndTime
        });
        builder.HasIndex(a => new
        {
            a.DoctorProfileId,
            a.AppointmentDate,
            a.StartTime
        })
            .IsUnique()
            .HasFilter("[Status] IN (0, 1)");
        builder.HasIndex(a => new { a.PatientProfileId, a.AppointmentDate });
        builder.HasIndex(a => a.RescheduledFromAppointmentId)
            .IsUnique()
            .HasFilter("[RescheduledFromAppointmentId] IS NOT NULL AND [Status] IN (0, 1)");

        builder.HasOne(a => a.PatientProfile)
            .WithMany()
            .HasForeignKey(a => a.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.DoctorProfile)
            .WithMany()
            .HasForeignKey(a => a.DoctorProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.RescheduledFromAppointment)
            .WithMany()
            .HasForeignKey(a => a.RescheduledFromAppointmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DoctorWorkingHourConfiguration : IEntityTypeConfiguration<DoctorWorkingHour>
{
    public void Configure(EntityTypeBuilder<DoctorWorkingHour> builder)
    {
        builder.ToTable("DoctorWorkingHours", table =>
            table.HasCheckConstraint(
                "CK_DoctorWorkingHours_TimeRange",
                "[EndTime] > [StartTime]"));

        builder.HasKey(item => item.Id);
        builder.Property(item => item.StartTime).HasColumnType("time(0)");
        builder.Property(item => item.EndTime).HasColumnType("time(0)");
        builder.HasIndex(item => new { item.DoctorProfileId, item.DayOfWeek, item.StartTime })
            .IsUnique();
        builder.HasOne(item => item.DoctorProfile)
            .WithMany()
            .HasForeignKey(item => item.DoctorProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DoctorBreakConfiguration : IEntityTypeConfiguration<DoctorBreak>
{
    public void Configure(EntityTypeBuilder<DoctorBreak> builder)
    {
        builder.ToTable("DoctorBreaks", table =>
            table.HasCheckConstraint(
                "CK_DoctorBreaks_TimeRange",
                "[EndTime] > [StartTime]"));

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Date).HasColumnType("date");
        builder.Property(item => item.StartTime).HasColumnType("time(0)");
        builder.Property(item => item.EndTime).HasColumnType("time(0)");
        builder.HasIndex(item => new { item.DoctorProfileId, item.Date, item.StartTime });
        builder.HasOne(item => item.DoctorProfile)
            .WithMany()
            .HasForeignKey(item => item.DoctorProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DoctorHolidayConfiguration : IEntityTypeConfiguration<DoctorHoliday>
{
    public void Configure(EntityTypeBuilder<DoctorHoliday> builder)
    {
        builder.ToTable("DoctorHolidays", table =>
            table.HasCheckConstraint(
                "CK_DoctorHolidays_DateRange",
                "[EndDate] >= [StartDate]"));

        builder.HasKey(item => item.Id);
        builder.Property(item => item.StartDate).HasColumnType("date");
        builder.Property(item => item.EndDate).HasColumnType("date");
        builder.Property(item => item.Reason).HasMaxLength(500);
        builder.HasIndex(item => new { item.DoctorProfileId, item.StartDate, item.EndDate });
        builder.HasOne(item => item.DoctorProfile)
            .WithMany()
            .HasForeignKey(item => item.DoctorProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class QueueEntryConfiguration : IEntityTypeConfiguration<QueueEntry>
{
    public void Configure(EntityTypeBuilder<QueueEntry> builder)
    {
        builder.ToTable("QueueEntries", table =>
            table.HasCheckConstraint(
                "CK_QueueEntries_SourceAppointmentSequence",
                "([Source] = 1 AND [AppointmentId] IS NULL AND [QueueSequence] IS NOT NULL AND [QueueSequence] > 0) OR " +
                "([Source] = 0 AND [AppointmentId] IS NOT NULL AND [QueueSequence] IS NULL)"));

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Source).HasConversion<int>();
        builder.Property(item => item.Status).HasConversion<int>().IsConcurrencyToken();
        builder.Property(item => item.QueueDate).HasColumnType("date");
        builder.HasIndex(item => item.AppointmentId)
            .IsUnique()
            .HasFilter("[AppointmentId] IS NOT NULL");
        builder.HasIndex(item => new { item.DoctorProfileId, item.QueueDate, item.QueueSequence })
            .IsUnique()
            .HasFilter("[QueueSequence] IS NOT NULL");
        builder.HasIndex(item => item.DoctorProfileId)
            .IsUnique()
            .HasFilter("[Status] = 2");
        builder.HasIndex(item => new
        {
            item.DoctorProfileId,
            item.Source,
            item.QueueDate,
            item.ArrivedAt,
            item.QueueSequence
        });

        builder.HasOne(item => item.PatientProfile)
            .WithMany()
            .HasForeignKey(item => item.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.DoctorProfile)
            .WithMany()
            .HasForeignKey(item => item.DoctorProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.Appointment)
            .WithMany()
            .HasForeignKey(item => item.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WalkInAdmissionApprovalConfiguration
    : IEntityTypeConfiguration<WalkInAdmissionApproval>
{
    public void Configure(EntityTypeBuilder<WalkInAdmissionApproval> builder)
    {
        builder.ToTable("WalkInAdmissionApprovals", table =>
            table.HasCheckConstraint(
                "CK_WalkInAdmissionApprovals_DecisionTimestamp",
                "[Decision] IN (0, 1, 2, 3) AND " +
                "(([Decision] = 0 AND [DecisionAtUtc] IS NULL AND [UsedAtUtc] IS NULL) OR " +
                "([Decision] = 1 AND [DecisionAtUtc] IS NOT NULL) OR " +
                "([Decision] IN (2, 3) AND [DecisionAtUtc] IS NOT NULL AND [UsedAtUtc] IS NULL)) AND " +
                "([UsedAtUtc] IS NULL OR [DecisionAtUtc] IS NULL OR [UsedAtUtc] >= [DecisionAtUtc])"));

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Decision).HasConversion<int>().IsConcurrencyToken();
        builder.Property(item => item.UsedAtUtc).IsConcurrencyToken();
        builder.Property(item => item.Notes).HasMaxLength(500);
        builder.HasIndex(item => item.QueueEntryId)
            .IsUnique()
            .HasFilter("[Decision] = 0");
        builder.HasIndex(item => new { item.DoctorProfileId, item.Decision, item.RequestedAtUtc });

        builder.HasOne(item => item.QueueEntry)
            .WithMany()
            .HasForeignKey(item => item.QueueEntryId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DoctorProfile>()
            .WithMany()
            .HasForeignKey(item => item.DoctorProfileId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
