using MediCare.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Infrastructure.Persistence.Configurations;

public sealed class MedicalRecordConfiguration : IEntityTypeConfiguration<MedicalRecord>
{
    public void Configure(EntityTypeBuilder<MedicalRecord> builder)
    {
        builder.ToTable("MedicalRecords");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Symptoms).IsRequired().HasMaxLength(2000);
        builder.Property(r => r.Diagnosis).IsRequired().HasMaxLength(2000);
        builder.Property(r => r.TreatmentPlan).HasMaxLength(2000);
        builder.Property(r => r.DoctorNotes).HasMaxLength(2000);

        // Medical history must survive: never cascade deletes into it.
        builder.HasOne<PatientProfile>()
            .WithMany()
            .HasForeignKey(r => r.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DoctorProfile>()
            .WithMany()
            .HasForeignKey(r => r.DoctorProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(r => r.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.PatientProfileId, r.CreatedAtUtc });
        builder.HasIndex(r => r.DoctorProfileId);
        builder.HasIndex(r => r.AppointmentId);
    }
}
