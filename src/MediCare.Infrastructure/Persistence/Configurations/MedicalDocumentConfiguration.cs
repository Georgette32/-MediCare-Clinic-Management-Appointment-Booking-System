using MediCare.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Infrastructure.Persistence.Configurations;

public sealed class MedicalDocumentConfiguration : IEntityTypeConfiguration<MedicalDocument>
{
    public void Configure(EntityTypeBuilder<MedicalDocument> builder)
    {
        builder.ToTable("MedicalDocuments");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.OriginalFileName).IsRequired().HasMaxLength(260);
        builder.Property(d => d.StoredFileName).IsRequired().HasMaxLength(260);
        builder.Property(d => d.ContentType).IsRequired().HasMaxLength(150);

        builder.HasOne<PatientProfile>()
            .WithMany()
            .HasForeignKey(d => d.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MedicalRecord>()
            .WithMany()
            .HasForeignKey(d => d.MedicalRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.PatientProfileId);
        builder.HasIndex(d => d.MedicalRecordId);
        builder.HasIndex(d => d.StoredFileName).IsUnique();
    }
}
