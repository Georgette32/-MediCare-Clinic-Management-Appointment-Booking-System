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
        builder.ToTable("DoctorProfiles");
        builder.HasKey(d => d.Id);
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
        builder.ToTable("Appointments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Status).HasConversion<int>();
        builder.Property(a => a.Fee).HasPrecision(18, 2);
        builder.Property(a => a.Reason).HasMaxLength(1000);
        builder.Property(a => a.DoctorNotes).HasMaxLength(2000);
        builder.HasIndex(a => a.ScheduledAtUtc);
        builder.HasIndex(a => a.Status);

        builder.HasOne(a => a.PatientProfile)
            .WithMany()
            .HasForeignKey(a => a.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.DoctorProfile)
            .WithMany()
            .HasForeignKey(a => a.DoctorProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
