using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

/// <summary>Read-only appointment and patient queries. Every query is AsNoTracking.</summary>
public sealed class EfAppointmentQueryRepository(MediCareDbContext dbContext) : IAppointmentQueryRepository
{
    public Task<Guid?> GetPatientIdForUserAsync(Guid applicationUserId, CancellationToken cancellationToken) =>
        dbContext.Patients.AsNoTracking()
            .Where(patient => patient.ApplicationUserId == applicationUserId)
            .Select(patient => (Guid?)patient.Id)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<PatientSearchRecord?> GetPatientAsync(Guid patientId, CancellationToken cancellationToken) =>
        PatientRows(dbContext.Patients.AsNoTracking().Where(patient => patient.Id == patientId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PatientSearchRecord>> SearchPatientsAsync(string? exactPhone, string? phoneDigits, string? nameContains, CancellationToken cancellationToken)
    {
        // Filters run on the Patient entity before the join; EF cannot translate a filter over a projected record.
        var patients = dbContext.Patients.AsNoTracking();
        if (exactPhone is not null)
            patients = patients.Where(patient => patient.PhoneNumber == exactPhone);
        else if (phoneDigits is not null)
            patients = patients.Where(patient => patient.PhoneNumber.Contains(phoneDigits));
        else if (nameContains is not null)
            patients = patients.Where(patient => patient.FullName.Contains(nameContains));
        else
            return [];

        var rows = await PatientRows(patients.OrderBy(patient => patient.FullName).Take(50)).ToArrayAsync(cancellationToken);
        return rows.OrderBy(row => row.FullName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public async Task<IReadOnlyList<AppointmentRecord>> GetForPatientAsync(Guid patientId, CancellationToken cancellationToken) =>
        Map(await Included(dbContext.Appointments.AsNoTracking().Where(item => item.PatientId == patientId))
            .ToArrayAsync(cancellationToken));

    public async Task<IReadOnlyList<AppointmentRecord>> GetForDateAsync(DateOnly date, Guid? doctorId, CancellationToken cancellationToken) =>
        Map(await Included(dbContext.Appointments.AsNoTracking()
                .Where(item => item.AppointmentDate == date && (doctorId == null || item.DoctorId == doctorId)))
            .OrderBy(item => item.StartTime)
            .ToArrayAsync(cancellationToken));

    public async Task<AppointmentRecord?> GetRecordAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var rows = Map(await Included(dbContext.Appointments.AsNoTracking().Where(item => item.Id == appointmentId))
            .ToArrayAsync(cancellationToken));
        return rows.SingleOrDefault();
    }

    public async Task<IReadOnlyList<LiveReplacementRecord>> GetLiveReplacementsAsync(Guid[] originalAppointmentIds, CancellationToken cancellationToken)
    {
        if (originalAppointmentIds.Length == 0)
            return [];

        return await dbContext.Appointments.AsNoTracking()
            .Where(item => item.RescheduledFromAppointmentId != null &&
                           originalAppointmentIds.Contains(item.RescheduledFromAppointmentId.Value) &&
                           (item.Status == AppointmentStatus.Pending || item.Status == AppointmentStatus.Confirmed))
            .Select(item => new LiveReplacementRecord(
                item.RescheduledFromAppointmentId!.Value,
                item.Id,
                item.AppointmentDate,
                item.StartTime,
                item.Status))
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>Loads the names and the original appointment link; the flat record is built in memory (EF cannot project conditional navigations here).</summary>
    private static IQueryable<Appointment> Included(IQueryable<Appointment> query) =>
        query.Include(item => item.Patient)
            .Include(item => item.Doctor).ThenInclude(doctor => doctor.Specialization)
            .Include(item => item.RescheduledFromAppointment);

    private static IReadOnlyList<AppointmentRecord> Map(IEnumerable<Appointment> rows) =>
        rows.Select(item => new AppointmentRecord(
            item.Id,
            item.PatientId,
            item.Patient.FullName,
            item.Patient.PhoneNumber,
            item.DoctorId,
            item.Doctor.FullName,
            item.Doctor.Specialization.Name,
            item.AppointmentDate,
            item.StartTime,
            item.EndTime,
            item.Status,
            item.CancellationReason,
            item.RescheduledFromAppointmentId,
            item.RescheduledFromAppointment?.AppointmentDate,
            item.RescheduledFromAppointment?.StartTime)).ToArray();

    private IQueryable<PatientSearchRecord> PatientRows(IQueryable<Patient> patients) =>
        patients.Join(
            dbContext.Users.AsNoTracking(),
            patient => patient.ApplicationUserId,
            user => user.Id,
            (patient, user) => new PatientSearchRecord(patient.Id, user.Id, patient.FullName, patient.PhoneNumber, user.Email));
}
