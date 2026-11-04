using MediCare.Domain;

namespace MediCare.Application;

public sealed record ConsultationStatistics(int Samples, double MeanMinutes, double SampleVariance, double StandardDeviation)
{
    public static ConsultationStatistics Calculate(IEnumerable<TimeSpan> durations)
    {
        var values = durations.Where(d => d > TimeSpan.Zero).TakeLast(30).Select(d => d.TotalMinutes).ToArray();
        if (values.Length == 0) return new(0, 0, 0, 0);
        var mean = values.Average();
        var variance = values.Length > 1 ? values.Sum(v => Math.Pow(v - mean, 2)) / (values.Length - 1) : 0;
        return new(values.Length, mean, variance, Math.Sqrt(variance));
    }
}

public static class SchedulingRules
{
    public static int GracePeriodMinutes(int consultationMinutes, decimal multiplier = 2m) =>
        (int)Math.Ceiling(consultationMinutes * multiplier);

    public static bool IsWithinGrace(DateTimeOffset scheduledStart, DateTimeOffset arrival, int consultationMinutes, decimal multiplier = 2m) =>
        arrival <= scheduledStart.AddMinutes(GracePeriodMinutes(consultationMinutes, multiplier));

    public static int EffectiveSlotMinutes(DoctorSchedulingSettings settings, ConsultationStatistics stats)
    {
        if (stats.Samples < settings.MinimumHistorySamples) return settings.DefaultConsultationDurationMinutes;
        // Adaptive guard uses variability; unusually long consultations remain in the sample and raise the buffer.
        return Math.Max(1, (int)Math.Ceiling(stats.MeanMinutes + (double)settings.SafetyBufferMultiplier * stats.StandardDeviation));
    }

    public static DateTimeOffset? EstimateStart(DateTimeOffset now, DateTimeOffset? currentConsultationStart,
        int? expectedRemainingMinutes, IEnumerable<int> durationsAhead)
    {
        var start = now;
        if (currentConsultationStart is not null && expectedRemainingMinutes is not null)
            start = start.AddMinutes(Math.Max(0, expectedRemainingMinutes.Value));
        return start.AddMinutes(durationsAhead.Sum());
    }
}

public static class QueuePolicy
{
    public static IOrderedEnumerable<QueueEntry> OrderEligible(IEnumerable<QueueEntry> entries) => entries
        .Where(q => q.Status == QueueStatus.Waiting && (q.Type == QueueType.Booked || q.Approval == WalkInApproval.Approved))
        .OrderBy(q => q.Type == QueueType.Booked ? 0 : 1)
        .ThenBy(q => q.Type == QueueType.Booked ? q.Appointment?.ScheduledStart ?? DateTimeOffset.MaxValue : q.ArrivalTime)
        .ThenBy(q => q.CheckInTime ?? q.ArrivalTime);
}

public sealed record QueueEstimate(long QueueEntryId, string PatientId, QueueType Type, DateTimeOffset EstimatedStartAt, int EstimatedWaitMinutes);

public static class QueueEstimateCalculator
{
    public static IReadOnlyList<QueueEstimate> Estimate(DateTimeOffset now, int expectedConsultationMinutes,
        QueueEntry? inConsultation, IEnumerable<QueueEntry> called, IEnumerable<QueueEntry> waiting)
    {
        var availableAt = now;
        if (inConsultation?.EnteredConsultationAt is { } startedAt)
            availableAt = Max(now, startedAt.AddMinutes(expectedConsultationMinutes));
        var ordered = called.OrderBy(x => x.CalledAt).Concat(QueuePolicy.OrderEligible(waiting));
        var result = new List<QueueEstimate>();
        foreach (var entry in ordered)
        {
            var scheduled = entry.Type == QueueType.Booked ? entry.Appointment?.ScheduledStart : null;
            var estimated = scheduled is { } start ? Max(availableAt, start) : availableAt;
            result.Add(new(entry.Id, entry.PatientId, entry.Type, estimated, Math.Max(0, (int)Math.Ceiling((estimated - now).TotalMinutes))));
            availableAt = estimated.AddMinutes(expectedConsultationMinutes);
        }
        return result;
    }

    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset second) => first >= second ? first : second;
}

public sealed record QueueSnapshot(string DoctorId, QueueEntry? InConsultation,
    IReadOnlyList<QueueEntry> BookedWaiting, IReadOnlyList<QueueEntry> Called,
    IReadOnlyList<QueueEntry> WalkIns, IReadOnlyList<QueueEntry> PendingWalkIns, int Occupancy, int Capacity, int ReservedBookedCapacity,
    IReadOnlyList<QueueEstimate> Estimates, DoctorQueueSession? CurrentSession,
    IReadOnlyList<DoctorQueueSession> Sessions);

public sealed record DoctorDirectoryItem(string DoctorId, string DisplayName, string? Specialization, decimal? ConsultationFee, int? DefaultConsultationDurationMinutes, bool SchedulingConfigured);
public sealed record ScheduleEvent(long Id, string PatientId, DateTimeOffset Start, DateTimeOffset End, string Status);

public interface IAppointmentService
{
    Task<IReadOnlyList<DateTimeOffset>> GetAvailableSlotsAsync(string doctorId, DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<Appointment>> GetPatientAppointmentsAsync(string patientId, DateOnly? from = null, DateOnly? to = null, AppointmentStatus? status = null, CancellationToken ct = default);
    Task<IReadOnlyList<AppointmentOffer>> GetPatientOffersAsync(string patientId, CancellationToken ct = default);
    Task<IReadOnlyList<AppointmentNotification>> GetPatientNotificationsAsync(string patientId, CancellationToken ct = default);
    Task MarkNotificationReadAsync(long notificationId, string patientId, CancellationToken ct = default);
    Task<IReadOnlyList<Appointment>> GetDoctorAppointmentsAsync(string doctorId, DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<ScheduleEvent>> GetScheduleEventsAsync(string doctorId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
    Task<IReadOnlyList<DoctorDirectoryItem>> SearchDoctorsAsync(string? search, CancellationToken ct = default);
    Task<DoctorSchedulingSettings?> GetDoctorSettingsAsync(string doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<DoctorWorkingHour>> GetDoctorWorkingHoursAsync(string doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<DoctorBreak>> GetDoctorBreaksAsync(string doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<DoctorLeave>> GetDoctorLeavesAsync(string doctorId, CancellationToken ct = default);
    Task SaveDoctorSettingsAsync(DoctorSchedulingSettings settings, CancellationToken ct = default);
    Task SaveWorkingHourAsync(string doctorId, DayOfWeek day, TimeOnly start, TimeOnly end, CancellationToken ct = default);
    Task AddBreakAsync(DoctorBreak item, CancellationToken ct = default);
    Task AddLeaveAsync(DoctorLeave item, CancellationToken ct = default);
    Task<Appointment> BookAsync(string patientId, string doctorId, DateTimeOffset start, CancellationToken ct = default);
    Task CancelAsync(long appointmentId, string patientId, string? reason, CancellationToken ct = default);
    Task RescheduleAsync(long appointmentId, string patientId, DateTimeOffset newStart, CancellationToken ct = default);
    Task CheckInAsync(long appointmentId, string patientId, DateTimeOffset arrivedAt, CancellationToken ct = default);
    Task<QueueEntry> RegisterWalkInAsync(string doctorId, string patientId, DateTimeOffset arrival, CancellationToken ct = default);
    Task ApproveWalkInAsync(long queueEntryId, string doctorId, bool approve, bool defer, CancellationToken ct = default);
    Task<DoctorQueueSession> CreateQueueSessionAsync(string doctorId, CancellationToken ct = default);
    Task<DoctorQueueSession> StartQueueSessionAsync(long sessionId, string doctorId, CancellationToken ct = default);
    Task<DoctorQueueSession> CloseQueueSessionAsync(long sessionId, string doctorId, CancellationToken ct = default);
    Task<DoctorQueueSession?> GetCurrentQueueSessionAsync(string doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<DoctorQueueSession>> GetDoctorQueueSessionsAsync(string doctorId, CancellationToken ct = default);
    Task<QueueEntry> CallNextAsync(string doctorId, CancellationToken ct = default);
    Task StartConsultationAsync(long queueEntryId, string doctorId, CancellationToken ct = default);
    Task CompleteConsultationAsync(long queueEntryId, string doctorId, CancellationToken ct = default);
    Task<QueueSnapshot> GetQueueAsync(string doctorId, CancellationToken ct = default);
    Task ExpireCallsAsync(string doctorId, CancellationToken ct = default);
    Task ExpireAllCallsAsync(CancellationToken ct = default);
    Task<WaitingListEntry> JoinWaitingListAsync(WaitingListEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AppointmentOffer>> CreateOffersForCancelledSlotAsync(Appointment appointment, int expirationMinutes = 15, CancellationToken ct = default);
    Task AcceptOfferAsync(long offerId, string patientId, CancellationToken ct = default);
    Task DeclineOfferAsync(long offerId, string patientId, CancellationToken ct = default);
    Task ExpireOffersAsync(CancellationToken ct = default);
}

public interface IAppointmentNotifier
{
    Task OfferCreatedAsync(string patientId, long offerId, DateTimeOffset expiresAt, CancellationToken ct = default);
}
