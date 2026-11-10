using MediCare.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

public sealed class AppointmentDbContext(DbContextOptions<AppointmentDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<DoctorSchedulingSettings> SchedulingSettings => Set<DoctorSchedulingSettings>();
    public DbSet<DoctorWorkingHour> WorkingHours => Set<DoctorWorkingHour>();
    public DbSet<DoctorBreak> Breaks => Set<DoctorBreak>();
    public DbSet<DoctorLeave> Leaves => Set<DoctorLeave>();
    public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
    public DbSet<DoctorQueueSession> DoctorQueueSessions => Set<DoctorQueueSession>();
    public DbSet<WaitingListEntry> WaitingList => Set<WaitingListEntry>();
    public DbSet<AppointmentOffer> Offers => Set<AppointmentOffer>();
    public DbSet<AppointmentEvent> Events => Set<AppointmentEvent>();
    public DbSet<AppointmentNotification> AppointmentNotifications => Set<AppointmentNotification>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Appointment>().HasIndex(x => new { x.DoctorId, x.ScheduledStart, x.ScheduledEnd });
        b.Entity<Appointment>().HasIndex(x => new { x.PatientId, x.Status });
        b.Entity<Appointment>().Property(x => x.RowVersion).IsRowVersion();
        b.Entity<Appointment>().ToTable(t => t.HasCheckConstraint("CK_Appointment_Time", "[ScheduledEnd] > [ScheduledStart]"));
        b.Entity<Appointment>().ToTable(t => t.HasCheckConstraint("CK_Appointment_DurationSnapshot", "[DurationSnapshotMinutes] >= 0"));
        b.Entity<DoctorSchedulingSettings>().HasKey(x => x.DoctorId);
        b.Entity<DoctorSchedulingSettings>().Property(x => x.ConsultationFee).HasPrecision(12, 2);
        b.Entity<DoctorSchedulingSettings>().Property(x => x.GracePeriodMultiplier).HasPrecision(5, 2);
        b.Entity<DoctorSchedulingSettings>().Property(x => x.SafetyBufferMultiplier).HasPrecision(5, 2);
        b.Entity<DoctorSchedulingSettings>().Property(x => x.RowVersion).IsRowVersion();
        b.Entity<DoctorWorkingHour>().HasIndex(x => new { x.DoctorId, x.DayOfWeek }).IsUnique();
        b.Entity<QueueEntry>().HasIndex(x => new { x.DoctorId, x.Status, x.ArrivalTime });
        b.Entity<QueueEntry>().HasIndex(x => new { x.AppointmentId, x.Type }).IsUnique().HasFilter("[AppointmentId] IS NOT NULL");
        b.Entity<QueueEntry>().Property(x => x.RowVersion).IsRowVersion();
        b.Entity<QueueEntry>().HasOne(x => x.Appointment).WithMany(x => x.QueueEntries).HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DoctorQueueSession>().Property(x => x.DoctorId).IsRequired();
        b.Entity<DoctorQueueSession>().Property(x => x.RowVersion).IsRowVersion();
        b.Entity<DoctorQueueSession>().ToTable(t => t.HasCheckConstraint("CK_DoctorQueueSession_State", "([Status] = 0 AND [StartedAt] IS NULL AND [ClosedAt] IS NULL) OR ([Status] = 1 AND [StartedAt] IS NOT NULL AND [ClosedAt] IS NULL) OR ([Status] = 2 AND [StartedAt] IS NOT NULL AND [ClosedAt] IS NOT NULL)"));
        b.Entity<DoctorQueueSession>().HasAlternateKey(x => new { x.DoctorId, x.Id });
        b.Entity<DoctorQueueSession>().HasIndex(x => new { x.DoctorId, x.Status })
            .HasDatabaseName("IX_DoctorQueueSessions_DoctorId_Active")
            .IsUnique()
            .HasFilter("[Status] = 1");
        b.Entity<DoctorQueueSession>().HasIndex(x => new { x.DoctorId, x.CreatedAt });
        b.Entity<DoctorQueueSession>().HasMany(x => x.QueueEntries).WithOne(x => x.DoctorQueueSession)
            .HasForeignKey(x => new { x.DoctorId, x.DoctorQueueSessionId })
            .HasPrincipalKey(x => new { x.DoctorId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<AppointmentOffer>().HasIndex(x => new { x.DoctorId, x.SlotStart, x.Status });
        b.Entity<AppointmentOffer>().Property(x => x.RowVersion).IsRowVersion();
        b.Entity<AppointmentOffer>().HasOne(x => x.WaitingListEntry).WithMany(x => x.Offers).HasForeignKey(x => x.WaitingListEntryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WaitingListEntry>().HasIndex(x => new { x.Status, x.CreatedAt });
        b.Entity<AppointmentNotification>().HasIndex(x => new { x.PatientId, x.ReadAt, x.CreatedAt });
        b.Entity<AppointmentNotification>().HasIndex(x => x.OfferId).IsUnique().HasFilter("[OfferId] IS NOT NULL");
        b.Entity<AppointmentNotification>().HasOne<AppointmentOffer>().WithMany().HasForeignKey(x => x.OfferId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AppointmentEvent>().HasIndex(x => new { x.DoctorId, x.OccurredAt });
        b.Entity<AppointmentEvent>().HasOne<DoctorQueueSession>().WithMany().HasForeignKey(x => new { x.DoctorId, x.DoctorQueueSessionId }).HasPrincipalKey(x => new { x.DoctorId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        base.OnModelCreating(b);
    }
}
