using MediCare.Domain.Entities;
using MediCare.Infrastructure.Identity;
using MediCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MediCare.Tests;

public sealed class PersistenceModelTests
{
    [Fact]
    public void Patient_and_doctor_have_required_unique_account_links()
    {
        using var context = CreateContext();
        var patient = context.Model.FindEntityType(typeof(Patient))!;
        var doctor = context.Model.FindEntityType(typeof(Doctor))!;

        AssertAccountLinkIsUnique(patient, nameof(Patient.ApplicationUserId));
        AssertAccountLinkIsUnique(doctor, nameof(Doctor.ApplicationUserId));
    }

    [Fact]
    public void Queue_model_keeps_appointment_link_optional_and_walk_in_sequence_nullable()
    {
        using var context = CreateContext();
        var queueEntry = context.Model.FindEntityType(typeof(QueueEntry))!;

        Assert.True(queueEntry.FindProperty(nameof(QueueEntry.AppointmentId))!.IsNullable);
        Assert.True(queueEntry.FindProperty(nameof(QueueEntry.QueueSequence))!.IsNullable);
        Assert.Contains(queueEntry.GetIndexes(), index =>
            index.IsUnique && index.Properties.Count == 1 && index.Properties[0].Name == nameof(QueueEntry.AppointmentId));
    }

    [Fact]
    public void Doctor_owns_configured_duration_not_working_hour()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var doctor = model.FindEntityType(typeof(Doctor))!;
        var workingHour = model.FindEntityType(typeof(DoctorWorkingHour))!;

        Assert.NotNull(doctor.FindProperty(nameof(Doctor.AppointmentDurationMinutes)));
        Assert.Null(workingHour.FindProperty("AppointmentDurationMinutes"));
        Assert.Contains(doctor.GetCheckConstraints(), constraint => constraint.Name == "CK_Doctors_AppointmentDurationPositive");
    }

    [Fact]
    public void Appointment_successor_unique_index_covers_only_live_replacements()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var appointment = model.FindEntityType(typeof(Appointment))!;
        var index = Assert.Single(appointment.GetIndexes(), item =>
            item.IsUnique && item.Properties.Any(property => property.Name == nameof(Appointment.RescheduledFromAppointmentId)));

        Assert.Contains("'Pending'", index.GetFilter());
        Assert.Contains("'Confirmed'", index.GetFilter());
        Assert.DoesNotContain("'Cancelled'", index.GetFilter());
        Assert.DoesNotContain("'Rejected'", index.GetFilter());
    }

    [Fact]
    public void Active_appointment_slots_have_a_filtered_unique_doctor_date_start_index()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var appointment = model.FindEntityType(typeof(Appointment))!;
        var index = Assert.Single(appointment.GetIndexes(), item =>
            item.IsUnique && item.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(Appointment.DoctorId), nameof(Appointment.AppointmentDate), nameof(Appointment.StartTime)
            }));

        Assert.Equal("[Status] IN ('Pending', 'Confirmed')", index.GetFilter());
    }

    [Fact]
    public void Active_consultation_unique_index_covers_only_in_consultation_entries()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var queue = model.FindEntityType(typeof(QueueEntry))!;
        var index = Assert.Single(queue.GetIndexes(), item =>
            item.IsUnique && item.Properties.Count == 1 && item.Properties[0].Name == nameof(QueueEntry.DoctorId));

        Assert.Equal("[Status] = 'InConsultation'", index.GetFilter());
    }

    [Fact]
    public void Schedule_entities_have_required_time_and_date_range_constraints()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        AssertConstraint(model, typeof(Appointment), "CK_Appointments_TimeRange", "[EndTime] > [StartTime]");
        AssertConstraint(model, typeof(DoctorBreak), "CK_DoctorBreaks_TimeRange", "[EndTime] > [StartTime]");
        AssertConstraint(model, typeof(DoctorWorkingHour), "CK_DoctorWorkingHours_TimeRange", "[EndTime] > [StartTime]");
        AssertConstraint(model, typeof(DoctorHoliday), "CK_DoctorHolidays_DateRange", "[EndDate] >= [StartDate]");
    }

    [Fact]
    public void Identity_name_length_matches_patient_and_doctor_profiles()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        var maxLength = model.FindEntityType(typeof(ApplicationUser))!.FindProperty(nameof(ApplicationUser.FullName))!.GetMaxLength();

        Assert.Equal(200, maxLength);
        Assert.Equal(maxLength, model.FindEntityType(typeof(Patient))!.FindProperty(nameof(Patient.FullName))!.GetMaxLength());
        Assert.Equal(maxLength, model.FindEntityType(typeof(Doctor))!.FindProperty(nameof(Doctor.FullName))!.GetMaxLength());
    }

    [Fact]
    public void Doctor_has_required_restricted_specialization_link_and_specialization_name_is_unique()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var doctor = model.FindEntityType(typeof(Doctor))!;
        var specialization = model.FindEntityType(typeof(Specialization))!;
        var foreignKey = Assert.Single(doctor.GetForeignKeys(), key => key.Properties.Any(property => property.Name == nameof(Doctor.SpecializationId)));

        Assert.Equal(typeof(Specialization), foreignKey.PrincipalEntityType.ClrType);
        Assert.True(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.Contains(specialization.GetIndexes(), index => index.IsUnique && index.Properties.Single().Name == nameof(Specialization.Name));
    }

    private static void AssertAccountLinkIsUnique(Microsoft.EntityFrameworkCore.Metadata.IEntityType entity, string propertyName)
    {
        var foreignKey = Assert.Single(entity.GetForeignKeys(), key => key.Properties.Any(property => property.Name == propertyName));
        Assert.Equal(typeof(ApplicationUser), foreignKey.PrincipalEntityType.ClrType);
        Assert.True(foreignKey.IsRequired);
        Assert.True(foreignKey.IsUnique);
    }

    private static void AssertConstraint(IModel model, Type entityType, string name, string sql)
    {
        var entity = model.FindEntityType(entityType)!;
        Assert.Contains(entity.GetCheckConstraints(), constraint => constraint.Name == name && constraint.Sql == sql);
    }

    private static MediCareDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MediCareDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=MediCareModelOnly;Trusted_Connection=True")
            .Options;
        return new MediCareDbContext(options);
    }
}
