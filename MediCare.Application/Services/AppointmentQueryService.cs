using MediCare.Application.Abstractions;

namespace MediCare.Application.Services;

/// <summary>An appointment row with the live replacement (if any) and the action flags computed on the clinic date.</summary>
public sealed record AppointmentRow(
    AppointmentRecord Record,
    LiveReplacementRecord? LiveReplacement,
    bool CanPatientCancel,
    bool CanPatientReschedule,
    bool CanClinicCancel,
    bool CanClinicReschedule);

public sealed record PatientAppointmentBoard(IReadOnlyList<AppointmentRow> Upcoming, IReadOnlyList<AppointmentRow> Past);

public interface IAppointmentQueryService
{
    Task<Guid?> GetPatientIdForUserAsync(Guid applicationUserId, CancellationToken cancellationToken);
    Task<PatientAppointmentBoard> GetPatientBoardAsync(Guid patientId, CancellationToken cancellationToken);
    /// <summary>Returns the appointment only when it belongs to the patient; otherwise null (no existence leak).</summary>
    Task<AppointmentRow?> GetOwnedAppointmentAsync(Guid appointmentId, Guid patientId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AppointmentRow>> GetDayAsync(DateOnly date, Guid? doctorId, CancellationToken cancellationToken);
    Task<AppointmentRow?> GetAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AppointmentRow>> GetPatientAppointmentsAsync(Guid patientId, CancellationToken cancellationToken);
    Task<PatientSearchRecord?> GetPatientAsync(Guid patientId, CancellationToken cancellationToken);
    /// <summary>Phone (any supported Egyptian format, or digits) or name search. Blank or too-short input returns nothing.</summary>
    Task<IReadOnlyList<PatientSearchRecord>> SearchPatientsAsync(string? query, CancellationToken cancellationToken);
}

public sealed class AppointmentQueryService(IAppointmentQueryRepository queries, IClinicClock clock) : IAppointmentQueryService
{
    private const int MinimumPhoneDigitsForSearch = 3;

    public Task<Guid?> GetPatientIdForUserAsync(Guid applicationUserId, CancellationToken cancellationToken) =>
        queries.GetPatientIdForUserAsync(applicationUserId, cancellationToken);

    public async Task<PatientAppointmentBoard> GetPatientBoardAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var rows = await BuildRowsAsync(await queries.GetForPatientAsync(patientId, cancellationToken), today, cancellationToken);
        return new PatientAppointmentBoard(
            rows.Where(row => row.Record.Date >= today)
                .OrderBy(row => row.Record.Date).ThenBy(row => row.Record.StartTime).ToArray(),
            rows.Where(row => row.Record.Date < today)
                .OrderByDescending(row => row.Record.Date).ThenByDescending(row => row.Record.StartTime).ToArray());
    }

    public async Task<AppointmentRow?> GetOwnedAppointmentAsync(Guid appointmentId, Guid patientId, CancellationToken cancellationToken)
    {
        var record = await queries.GetRecordAsync(appointmentId, cancellationToken);
        if (record is null || record.PatientId != patientId)
            return null;
        return (await BuildRowsAsync([record], clock.Today, cancellationToken)).Single();
    }

    public async Task<IReadOnlyList<AppointmentRow>> GetDayAsync(DateOnly date, Guid? doctorId, CancellationToken cancellationToken) =>
        await BuildRowsAsync(await queries.GetForDateAsync(date, doctorId, cancellationToken), clock.Today, cancellationToken);

    public async Task<AppointmentRow?> GetAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var record = await queries.GetRecordAsync(appointmentId, cancellationToken);
        return record is null ? null : (await BuildRowsAsync([record], clock.Today, cancellationToken)).Single();
    }

    public async Task<IReadOnlyList<AppointmentRow>> GetPatientAppointmentsAsync(Guid patientId, CancellationToken cancellationToken) =>
        await BuildRowsAsync(await queries.GetForPatientAsync(patientId, cancellationToken), clock.Today, cancellationToken);

    public Task<PatientSearchRecord?> GetPatientAsync(Guid patientId, CancellationToken cancellationToken) =>
        queries.GetPatientAsync(patientId, cancellationToken);

    public Task<IReadOnlyList<PatientSearchRecord>> SearchPatientsAsync(string? query, CancellationToken cancellationToken)
    {
        var text = (query ?? string.Empty).Trim();
        if (text.Length == 0)
            return Task.FromResult<IReadOnlyList<PatientSearchRecord>>([]);

        if (EgyptianMobileNumber.TryNormalize(text, out var canonical, out _))
            return queries.SearchPatientsAsync(canonical, null, null, cancellationToken);

        var digits = new string(text.Where(char.IsDigit).ToArray());
        var looksLikePhone = text.All(character => char.IsDigit(character) || character is '+' or ' ' or '-' or '(' or ')');
        if (looksLikePhone)
        {
            return digits.Length < MinimumPhoneDigitsForSearch
                ? Task.FromResult<IReadOnlyList<PatientSearchRecord>>([])
                : queries.SearchPatientsAsync(null, digits, null, cancellationToken);
        }

        return queries.SearchPatientsAsync(null, null, text, cancellationToken);
    }

    private async Task<IReadOnlyList<AppointmentRow>> BuildRowsAsync(IReadOnlyList<AppointmentRecord> records, DateOnly today, CancellationToken cancellationToken)
    {
        if (records.Count == 0)
            return [];

        var replacements = await queries.GetLiveReplacementsAsync(records.Select(record => record.Id).ToArray(), cancellationToken);
        var byOriginal = replacements
            .GroupBy(replacement => replacement.OriginalAppointmentId)
            .ToDictionary(group => group.Key, group => group.First());

        return records.Select(record =>
        {
            var live = byOriginal.GetValueOrDefault(record.Id);
            var hasLive = live is not null;
            return new AppointmentRow(
                record,
                live,
                AppointmentPolicy.CanPatientCancel(record.Status, record.Date, today),
                AppointmentPolicy.CanPatientReschedule(record.Status, record.Date, today, hasLive),
                AppointmentPolicy.CanClinicCancel(record.Status, record.Date, today),
                AppointmentPolicy.CanClinicReschedule(record.Status, record.Date, today, hasLive));
        }).ToArray();
    }
}
