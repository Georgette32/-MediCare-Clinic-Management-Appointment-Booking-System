using MediCare.Domain.Entities;
using MediCare.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MediCare.Infrastructure.Persistence;

public sealed class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Specialization> Specializations => Set<Specialization>();

    public DbSet<DoctorProfile> DoctorProfiles => Set<DoctorProfile>();

    public DbSet<PatientProfile> PatientProfiles => Set<PatientProfile>();

    public DbSet<DoctorAvailability> DoctorAvailabilities => Set<DoctorAvailability>();

    public DbSet<DoctorWorkingHour> DoctorWorkingHours => Set<DoctorWorkingHour>();

    public DbSet<DoctorBreak> DoctorBreaks => Set<DoctorBreak>();

    public DbSet<DoctorHoliday> DoctorHolidays => Set<DoctorHoliday>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();

    public DbSet<WalkInAdmissionApproval> WalkInAdmissionApprovals => Set<WalkInAdmissionApproval>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AppointmentReminder> AppointmentReminders => Set<AppointmentReminder>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("MEDICARE_CONNECTION")
            ?? "Server=(localdb)\\mssqllocaldb;Database=MediCareDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .Options;

        return new ApplicationDbContext(options);
    }
}
