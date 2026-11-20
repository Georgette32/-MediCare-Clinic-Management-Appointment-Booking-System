using MediCare.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Infrastructure.Persistence.Configurations;

public sealed class PrescriptionConfiguration : IEntityTypeConfiguration<Prescription>
{
    public void Configure(EntityTypeBuilder<Prescription> builder)
    {
        builder.ToTable("Prescriptions");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Notes).HasMaxLength(2000);

        builder.HasOne<PatientProfile>()
            .WithMany()
            .HasForeignKey(p => p.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DoctorProfile>()
            .WithMany()
            .HasForeignKey(p => p.DoctorProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MedicalRecord>()
            .WithMany()
            .HasForeignKey(p => p.MedicalRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        // Items live and die with their prescription.
        builder.HasMany(p => p.Items)
            .WithOne()
            .HasForeignKey(i => i.PrescriptionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Items)
            .HasField("_items")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(p => new { p.PatientProfileId, p.IssuedAtUtc });
        builder.HasIndex(p => p.DoctorProfileId);
        builder.HasIndex(p => p.MedicalRecordId);
    }
}

public sealed class PrescriptionItemConfiguration : IEntityTypeConfiguration<PrescriptionItem>
{
    public void Configure(EntityTypeBuilder<PrescriptionItem> builder)
    {
        builder.ToTable("PrescriptionItems");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.MedicationName).IsRequired().HasMaxLength(200);
        builder.Property(i => i.Dosage).IsRequired().HasMaxLength(100);
        builder.Property(i => i.Frequency).IsRequired().HasMaxLength(100);
        builder.Property(i => i.Instructions).HasMaxLength(500);
    }
}
