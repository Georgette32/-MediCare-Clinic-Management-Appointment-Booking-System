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
    public DbSet<DoctorBreak> DoctorBreaks => Set<DoctorBreak>();
    public DbSet<DoctorHoliday> DoctorHolidays => Set<DoctorHoliday>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>().Property(item => item.FullName).HasMaxLength(200).IsRequired();

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
            entity.HasOne(item => item.Specialization).WithMany().HasForeignKey(item => item.SpecializationId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => item.ApplicationUserId).IsUnique();
            entity.HasOne<ApplicationUser>().WithOne().HasForeignKey<Doctor>(item => item.ApplicationUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint("CK_Doctors_AppointmentDurationPositive", "[AppointmentDurationMinutes] > 0"));
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

        builder.Entity<DoctorBreak>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.DoctorId, item.Date, item.StartTime });
            entity.HasOne(item => item.Doctor).WithMany().HasForeignKey(item => item.DoctorId).OnDelete(DeleteBehavior.Restrict);
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
            entity.ToTable(table => table.HasCheckConstraint("CK_Appointments_TimeRange", "[EndTime] > [StartTime]"));
        });

        builder.Entity<QueueEntry>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Source).HasConversion<string>().HasMaxLength(16);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);
            entity.HasIndex(item => item.AppointmentId).IsUnique().HasFilter("[AppointmentId] IS NOT NULL");
            entity.HasIndex(item => new { item.DoctorId, item.QueueDate, item.QueueSequence }).IsUnique().HasFilter("[QueueSequence] IS NOT NULL");
            entity.HasIndex(item => item.DoctorId).IsUnique().HasFilter("[Status] = 'InConsultation'");
            entity.HasIndex(item => new { item.DoctorId, item.Source, item.QueueDate, item.ArrivedAt, item.QueueSequence });
            entity.HasOne(item => item.Patient).WithMany().HasForeignKey(item => item.PatientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Doctor).WithMany().HasForeignKey(item => item.DoctorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Appointment).WithMany().HasForeignKey(item => item.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_QueueEntries_SourceAppointmentSequence",
                "([Source] = 'WalkIn' AND [AppointmentId] IS NULL AND [QueueSequence] IS NOT NULL AND [QueueSequence] > 0) OR ([Source] = 'Scheduled' AND [AppointmentId] IS NOT NULL AND [QueueSequence] IS NULL)"));
        });
    }
}
