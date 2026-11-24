using MediCare.Application.Abstractions;
using MediCare.Application.Analytics;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Time;

namespace MediCare.Tests;

/// <summary>Hand-computed checks for the pure analytics engine (M/G/1, open time, sweep, metrics, hourly profile).</summary>
public sealed class AnalyticsCalculatorTests
{
    private static readonly TimeZoneInfo Cairo = ClinicTimeZoneResolver.Resolve("Africa/Cairo");
    private static readonly SystemClinicClock Clock = new(TimeProvider.System, Cairo);
    private static readonly Func<DateOnly, TimeOnly, DateTimeOffset> ToUtc = Clock.ToUtc;

    // Monday 7 December 2026 (Cairo is UTC+2 in December, so 09:00 local is 07:00 UTC).
    private static readonly DateOnly Day = new(2026, 12, 7);
    private static readonly DateOnly Today = new(2026, 12, 8);
    private static readonly Guid DoctorId = Guid.NewGuid();
    private static readonly AnalyticsDoctor Doctor = new(DoctorId, Guid.NewGuid(), "Dr. Test", 20, true);

    private static DateTimeOffset Local(DateOnly day, int hour, int minute) => ToUtc(day, new TimeOnly(hour, minute));

    private static AnalyticsWorkingHourRow Window(DateOnly day, int startHour, int endHour) =>
        new(DoctorId, day.DayOfWeek, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

    private static AnalyticsVisitRow Served(DateOnly day, DateTimeOffset arrived, DateTimeOffset started, DateTimeOffset completed) =>
        new(DoctorId, Guid.NewGuid(), day, QueueEntrySource.Scheduled, QueueEntryStatus.Completed, arrived, started.AddMinutes(-1), started, completed, false);

    private static AnalyticsRequest Request(DateOnly from, DateOnly to, DateTimeOffset? cutoff = null, int capMinutes = 60) =>
        new(from, to, Today, cutoff, TimeSpan.FromMinutes(capMinutes), Cairo, ToUtc);

    private static AnalyticsSnapshot Snapshot(
        IEnumerable<AnalyticsVisitRow>? visits = null,
        IEnumerable<AnalyticsAppointmentRow>? appointments = null,
        IEnumerable<AnalyticsWorkingHourRow>? hours = null,
        IEnumerable<AnalyticsHolidayRow>? holidays = null,
        IEnumerable<AnalyticsExtensionRow>? extensions = null,
        IEnumerable<AnalyticsBreakRow>? breaks = null) =>
        new([Doctor],
            visits?.ToArray() ?? [],
            appointments?.ToArray() ?? [],
            hours?.ToArray() ?? [],
            holidays?.ToArray() ?? [],
            extensions?.ToArray() ?? [],
            breaks?.ToArray() ?? []);

    private static DoctorQueueingMetrics MetricsFor(AnalyticsSnapshot snapshot, AnalyticsRequest request) =>
        AnalyticsCalculator.Calculate(snapshot, request).Single().Metrics;

    // ---------- M/G/1 ----------

    [Fact]
    public void Mg1_matches_hand_computed_example()
    {
        // lambda = 2/h, mean service = 20 min = 1/3 h, sigma^2 = 1/36 h^2.
        // rho = 2/3, lambda^2 sigma^2 = 1/9, rho^2 = 4/9, Lq = (5/9) / (2/3) = 5/6,
        // Wq = Lq / lambda = 5/12 h (25 min), Ws = 5/12 + 1/3 = 3/4 h (45 min), Ls = 5/6 + 2/3 = 3/2.
        var result = MG1Model.Compute(2.0, 1.0 / 3.0, 1.0 / 36.0, 10);

        Assert.Equal(TheoreticalStatus.Computed, result.Status);
        Assert.Equal(5.0 / 6.0, result.Lq!.Value, 6);
        Assert.Equal(5.0 / 12.0, result.WqHours!.Value, 6);
        Assert.Equal(0.75, result.WsHours!.Value, 6);
        Assert.Equal(1.5, result.Ls!.Value, 6);
    }

    [Fact]
    public void Mg1_with_exponential_service_reduces_to_mm1()
    {
        // sigma = 1/mu makes the formula equal Lq = rho^2 / (1 - rho). lambda = 2, mu = 3: rho = 2/3, Lq = 4/3.
        var result = MG1Model.Compute(2.0, 1.0 / 3.0, 1.0 / 9.0, 50);

        Assert.Equal(4.0 / 3.0, result.Lq!.Value, 9);
        Assert.Equal(result.Lq!.Value / 2.0, result.WqHours!.Value, 9);
    }

    [Fact]
    public void Mg1_at_or_above_full_utilization_is_overloaded_with_no_theoretical_numbers()
    {
        // lambda = 3, mean service 20 min: rho = exactly 1.
        var atOne = MG1Model.Compute(3.0, 1.0 / 3.0, 1.0 / 36.0, 10);
        var above = MG1Model.Compute(4.0, 1.0 / 3.0, 1.0 / 36.0, 10);

        Assert.Equal(TheoreticalStatus.Overloaded, atOne.Status);
        Assert.Null(atOne.Lq);
        Assert.Null(atOne.WqHours);
        Assert.Null(atOne.WsHours);
        Assert.Null(atOne.Ls);
        Assert.Equal(TheoreticalStatus.Overloaded, above.Status);
        Assert.Null(above.Lq);
    }

    [Fact]
    public void Mg1_with_fewer_than_two_observations_is_insufficient_data()
    {
        var result = MG1Model.Compute(2.0, 1.0 / 3.0, 0, 1);

        Assert.Equal(TheoreticalStatus.InsufficientData, result.Status);
        Assert.Contains("two completed consultations", result.Reason);
        Assert.Null(result.Lq);
    }

    [Fact]
    public void Mg1_with_no_arrivals_has_no_queue()
    {
        var result = MG1Model.Compute(0, 1.0 / 3.0, 1.0 / 36.0, 10);

        Assert.Equal(TheoreticalStatus.Computed, result.Status);
        Assert.Equal(0, result.Lq);
        Assert.Equal(0, result.WqHours);
        Assert.Equal(1.0 / 3.0, result.WsHours!.Value, 9);
    }

    // ---------- Open time T ----------

    [Fact]
    public void Open_time_is_windows_minus_breaks_with_an_approved_extension()
    {
        var hours = new[] { Window(Day, 9, 13) };
        var breaks = new[] { new AnalyticsBreakRow(DoctorId, Day, new TimeOnly(10, 0), new TimeOnly(10, 30)) };
        // The extension moves the 13:00 end to 14:00 (approved only; a pending request is not passed in).
        var extensions = new[] { new AnalyticsExtensionRow(DoctorId, Day, new TimeOnly(13, 0), new TimeOnly(14, 0)) };

        var open = OpenTimeCalculator.Build(Day, Day, ToUtc, hours, [], extensions, breaks, cutoffUtc: null);

        // 09:00-14:00 is 5 hours, minus the 30-minute break = 4.5 hours.
        Assert.Equal(4.5, OpenTimeCalculator.TotalHours(open), 9);
    }

    [Fact]
    public void Open_time_is_clipped_to_now_for_live_results()
    {
        var hours = new[] { Window(Day, 9, 13) };
        var breaks = new[] { new AnalyticsBreakRow(DoctorId, Day, new TimeOnly(10, 0), new TimeOnly(10, 30)) };

        // Live cutoff at 11:00 local: 09:00-11:00 is 2 hours, minus the 30-minute break = 1.5 hours.
        var open = OpenTimeCalculator.Build(Day, Day, ToUtc, hours, [], [], breaks, cutoffUtc: Local(Day, 11, 0));

        Assert.Equal(1.5, OpenTimeCalculator.TotalHours(open), 9);
    }

    [Fact]
    public void A_holiday_removes_the_whole_day()
    {
        var hours = new[] { Window(Day, 9, 13) };
        var holidays = new[] { new AnalyticsHolidayRow(DoctorId, Day, Day) };

        var open = OpenTimeCalculator.Build(Day, Day, ToUtc, hours, holidays, [], [], cutoffUtc: null);

        Assert.Empty(open);
    }

    [Fact]
    public void Open_time_on_a_daylight_saving_day_uses_real_local_windows()
    {
        // Egypt starts daylight saving on Friday 24 April 2026 at 00:00 local (clocks go to 01:00). A 09:00-13:00 window
        // is still four hours of open time, and it starts at 06:00 UTC because the offset is then UTC+3.
        var dstDay = new DateOnly(2026, 4, 24);
        var hours = new[] { new AnalyticsWorkingHourRow(DoctorId, dstDay.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(13, 0)) };

        var open = OpenTimeCalculator.Build(dstDay, dstDay, ToUtc, hours, [], [], [], cutoffUtc: null);

        Assert.Equal(4.0, OpenTimeCalculator.TotalHours(open), 9);
        Assert.Equal(new DateTimeOffset(2026, 4, 24, 6, 0, 0, TimeSpan.Zero), open[0].Start);
    }

    // ---------- Time-average sweep and Little's Law ----------

    [Fact]
    public void Little_law_holds_on_a_closed_timeline()
    {
        // Every patient arrives, starts and completes inside the 09:00-13:00 window (T = 4 h).
        var visits = new[]
        {
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 0), Local(Day, 9, 20)),   // wait 0, service 20, in clinic 20
            Served(Day, Local(Day, 9, 10), Local(Day, 9, 20), Local(Day, 9, 40)), // wait 10, service 20, in clinic 30
            Served(Day, Local(Day, 9, 30), Local(Day, 9, 40), Local(Day, 10, 10)) // wait 10, service 30, in clinic 40
        };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)]), Request(Day, Day));

        Assert.Equal(3, metrics.ArrivalsCount);
        Assert.Equal(0.75, metrics.LambdaPerHour!.Value, 9);
        // Time-average Ls = sum of sojourns / T = 90 min / 240 min = 0.375; Little: lambda * Ws = 0.75 * 0.5 h = 0.375.
        Assert.Equal(0.375, metrics.MeasuredLs!.Value, 9);
        Assert.Equal(0.375, metrics.LittleLs!.Value, 9);
        Assert.Equal(0.0, metrics.LittleLsCheckPercent!.Value, 6);
        // Time-average Lq = 20 min / 240 min = 1/12; Little: lambda * Wq = 0.75 * (20/3)/60 = 1/12.
        Assert.Equal(1.0 / 12.0, metrics.MeasuredLq!.Value, 9);
        Assert.Equal(0.0, metrics.LittleLqCheckPercent!.Value, 6);
        Assert.Equal(20.0 / 3.0, metrics.MeasuredWqMinutes!.Value, 9);
        Assert.Equal(30.0, metrics.MeasuredWsMinutes!.Value, 9);
    }

    [Fact]
    public void Measured_service_statistics_and_utilization_match_the_hand_computation()
    {
        var visits = new[]
        {
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 0), Local(Day, 9, 20)),
            Served(Day, Local(Day, 9, 10), Local(Day, 9, 20), Local(Day, 9, 40)),
            Served(Day, Local(Day, 9, 30), Local(Day, 9, 40), Local(Day, 10, 10))
        };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)]), Request(Day, Day));

        // Services 20, 20, 30 min: mean 70/3 min; sample variance = (200/3) / 2 = 100/3 min^2.
        Assert.Equal(70.0 / 3.0, metrics.MeanServiceMinutes!.Value, 9);
        Assert.Equal(Math.Sqrt(100.0 / 3.0), metrics.ServiceStdDevMinutes!.Value, 9);
        Assert.Equal(60.0 / (70.0 / 3.0), metrics.MuPerHour!.Value, 9);
        // rho = lambda / mu = 0.75 * 7/18 = 7/24; utilization = 70 min / 240 min = 7/24.
        Assert.Equal(7.0 / 24.0, metrics.Rho!.Value, 9);
        Assert.Equal(7.0 / 24.0, metrics.Utilization!.Value, 9);
        Assert.False(metrics.Overloaded);
    }

    [Fact]
    public void Theoretical_values_are_computed_from_measured_inputs_when_rho_is_below_one()
    {
        var visits = new[]
        {
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 0), Local(Day, 9, 20)),
            Served(Day, Local(Day, 9, 10), Local(Day, 9, 20), Local(Day, 9, 40)),
            Served(Day, Local(Day, 9, 30), Local(Day, 9, 40), Local(Day, 10, 10))
        };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)]), Request(Day, Day));

        // Hand value: Lq = (lambda^2 sigma^2 + rho^2) / (2(1 - rho)) = (1/192 + 49/576) / (17/12) = 13/204.
        Assert.Equal(TheoreticalStatus.Computed, metrics.TheoreticalStatus);
        Assert.Equal(13.0 / 204.0, metrics.TheoreticalLq!.Value, 9);
    }

    [Fact]
    public void Overloaded_doctor_is_flagged_and_has_no_theoretical_values()
    {
        // One open hour (09:00-10:00). Three patients, each served for 20 minutes: lambda = 3/h, mu = 3/h, rho = 1.
        var visits = new[]
        {
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 0), Local(Day, 9, 20)),
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 20), Local(Day, 9, 40)),
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 40), Local(Day, 10, 0))
        };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 10)]), Request(Day, Day));

        Assert.True(metrics.Overloaded);
        Assert.Equal(TheoreticalStatus.Overloaded, metrics.TheoreticalStatus);
        Assert.Null(metrics.TheoreticalLq);
        Assert.Null(metrics.TheoreticalWqMinutes);
        Assert.Null(metrics.TheoreticalWsMinutes);
        Assert.Null(metrics.TheoreticalLs);
    }

    [Fact]
    public void A_single_served_patient_gives_insufficient_data_for_theoretical_values()
    {
        var visits = new[] { Served(Day, Local(Day, 9, 0), Local(Day, 9, 0), Local(Day, 9, 20)) };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)]), Request(Day, Day));

        Assert.Equal(TheoreticalStatus.InsufficientData, metrics.TheoreticalStatus);
        Assert.Contains("two completed consultations", metrics.TheoreticalReason);
        Assert.Null(metrics.TheoreticalLq);
        Assert.False(metrics.Overloaded);
    }

    // ---------- Exclusions ----------

    [Fact]
    public void Clinic_unavailable_attempts_are_excluded_and_counted()
    {
        var excluded = new AnalyticsVisitRow(DoctorId, Guid.NewGuid(), Day, QueueEntrySource.Scheduled, QueueEntryStatus.Cancelled,
            Local(Day, 9, 0), null, null, null, ClinicUnavailable: true);
        var kept = new AnalyticsVisitRow(DoctorId, Guid.NewGuid(), Day, QueueEntrySource.WalkIn, QueueEntryStatus.Cancelled,
            Local(Day, 9, 0), null, null, null, ClinicUnavailable: false);

        var metrics = MetricsFor(Snapshot([excluded, kept], hours: [Window(Day, 9, 13)]), Request(Day, Day));

        Assert.Equal(1, metrics.ArrivalsCount);
        Assert.Equal(1, metrics.ExcludedClinicUnavailable);
    }

    [Fact]
    public void Service_durations_at_or_above_the_cap_are_excluded_and_counted()
    {
        var visits = new[]
        {
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 0), Local(Day, 9, 20)), // 20 min, kept
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 20), Local(Day, 10, 20)), // 60 min: not strictly below the cap
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 20), Local(Day, 10, 50))  // 90 min, excluded
        };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)]), Request(Day, Day));

        Assert.Equal(3, metrics.ServedCount);
        Assert.Equal(1, metrics.ServiceSampleCount);
        Assert.Equal(2, metrics.ExcludedServiceAtOrAboveCap);
        Assert.Equal(20.0, metrics.MeanServiceMinutes!.Value, 9);
        Assert.Null(metrics.ServiceStdDevMinutes);
    }

    [Fact]
    public void Completed_attempts_with_invalid_timestamps_are_excluded_and_counted()
    {
        var missingStart = new AnalyticsVisitRow(DoctorId, Guid.NewGuid(), Day, QueueEntrySource.Scheduled, QueueEntryStatus.Completed,
            Local(Day, 9, 0), null, null, Local(Day, 9, 20), false);
        var startedBeforeArrival = new AnalyticsVisitRow(DoctorId, Guid.NewGuid(), Day, QueueEntrySource.Scheduled, QueueEntryStatus.Completed,
            Local(Day, 9, 30), null, Local(Day, 9, 10), Local(Day, 9, 40), false);

        var metrics = MetricsFor(Snapshot([missingStart, startedBeforeArrival], hours: [Window(Day, 9, 13)]), Request(Day, Day));

        Assert.Equal(0, metrics.ServedCount);
        Assert.Equal(2, metrics.ExcludedInvalidTimestamps);
    }

    // ---------- No-show and walk-ins ----------

    [Fact]
    public void No_show_rate_counts_only_final_attendance_outcomes()
    {
        AnalyticsAppointmentRow Row(AppointmentStatus status, bool clinicUnavailable = false) =>
            new(DoctorId, Day, new TimeOnly(9, 0), status, clinicUnavailable);

        var appointments = new[]
        {
            Row(AppointmentStatus.NoShow),
            Row(AppointmentStatus.Completed),
            Row(AppointmentStatus.Completed),
            Row(AppointmentStatus.Confirmed),
            Row(AppointmentStatus.Pending),
            Row(AppointmentStatus.Cancelled),
            Row(AppointmentStatus.Cancelled, clinicUnavailable: true),
            Row(AppointmentStatus.Rejected),
            Row(AppointmentStatus.Rescheduled)
        };
        var metrics = MetricsFor(Snapshot(appointments: appointments, hours: [Window(Day, 9, 13)]), Request(Day, Day));

        Assert.Equal(1, metrics.NoShowCount);
        // Denominator = Completed + NoShow only; Pending and unresolved Confirmed are excluded.
        Assert.Equal(3, metrics.NoShowDenominator);
        Assert.Equal(1.0 / 3.0, metrics.NoShowRate!.Value, 9);
    }

    [Fact]
    public void Appointments_after_today_do_not_count_toward_the_no_show_rate()
    {
        var future = new AnalyticsAppointmentRow(DoctorId, Today.AddDays(1), new TimeOnly(9, 0), AppointmentStatus.NoShow, false);
        var request = new AnalyticsRequest(Day, Today.AddDays(1), Today, null, TimeSpan.FromMinutes(60), Cairo, ToUtc);

        var metrics = MetricsFor(Snapshot(appointments: [future], hours: [Window(Day, 9, 13)]), request);

        Assert.Equal(0, metrics.NoShowDenominator);
        Assert.Null(metrics.NoShowRate);
    }

    [Fact]
    public void Walk_in_throughput_counts_completed_walk_ins_per_open_hour()
    {
        var walkIn = new AnalyticsVisitRow(DoctorId, Guid.NewGuid(), Day, QueueEntrySource.WalkIn, QueueEntryStatus.Completed,
            Local(Day, 9, 0), Local(Day, 9, 0), Local(Day, 9, 0), Local(Day, 9, 20), false);
        var scheduled = new AnalyticsVisitRow(DoctorId, Guid.NewGuid(), Day, QueueEntrySource.Scheduled, QueueEntryStatus.Completed,
            Local(Day, 9, 0), Local(Day, 9, 0), Local(Day, 9, 20), Local(Day, 9, 40), false);

        var metrics = MetricsFor(Snapshot([walkIn, scheduled], hours: [Window(Day, 9, 13)]), Request(Day, Day));

        Assert.Equal(1, metrics.WalkInCompletedCount);
        Assert.Equal(0.25, metrics.WalkInPerHour!.Value, 9);
    }

    // ---------- Hourly profile ----------

    [Fact]
    public void Hourly_profile_uses_clinic_local_hours_across_the_spring_forward_day()
    {
        var dstDay = new DateOnly(2026, 4, 24);
        // Before the switch at 00:00 local (22:00 UTC on 23 April) the offset is UTC+2; after it, UTC+3.
        var arrivals = new[]
        {
            new AnalyticsVisitRow(DoctorId, Guid.NewGuid(), dstDay, QueueEntrySource.WalkIn, QueueEntryStatus.Waiting,
                new DateTimeOffset(2026, 4, 23, 21, 30, 0, TimeSpan.Zero), null, null, null, false), // 23:30 local
            new AnalyticsVisitRow(DoctorId, Guid.NewGuid(), dstDay, QueueEntrySource.WalkIn, QueueEntryStatus.Waiting,
                new DateTimeOffset(2026, 4, 23, 22, 30, 0, TimeSpan.Zero), null, null, null, false), // 01:30 local (after the gap)
            new AnalyticsVisitRow(DoctorId, Guid.NewGuid(), dstDay, QueueEntrySource.WalkIn, QueueEntryStatus.Waiting,
                new DateTimeOffset(2026, 4, 23, 23, 30, 0, TimeSpan.Zero), null, null, null, false)  // 02:30 local
        };
        var request = new AnalyticsRequest(dstDay, dstDay, Today, null, TimeSpan.FromMinutes(60), Cairo, ToUtc);

        var hours = AnalyticsCalculator.Calculate(
            Snapshot(arrivals, hours: [new AnalyticsWorkingHourRow(DoctorId, dstDay.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(13, 0))]),
            request).Single().Hours;

        Assert.Equal(1, hours[23].ActualArrivals);
        Assert.Equal(1, hours[1].ActualArrivals);
        Assert.Equal(1, hours[2].ActualArrivals);
        Assert.Equal(3, hours.Sum(item => item.ActualArrivals));
        Assert.Equal(60.0, hours[9].OpenMinutes, 6);
        Assert.Equal(60.0, hours[12].OpenMinutes, 6);
    }

    [Fact]
    public void Hourly_profile_counts_scheduled_starts_and_busy_minutes_per_hour()
    {
        var appointments = new[]
        {
            new AnalyticsAppointmentRow(DoctorId, Day, new TimeOnly(9, 0), AppointmentStatus.Confirmed, false),
            new AnalyticsAppointmentRow(DoctorId, Day, new TimeOnly(9, 20), AppointmentStatus.NoShow, false),
            new AnalyticsAppointmentRow(DoctorId, Day, new TimeOnly(10, 0), AppointmentStatus.Pending, false)
        };
        var visits = new[] { Served(Day, Local(Day, 9, 0), Local(Day, 9, 0), Local(Day, 9, 20)) };

        var hours = AnalyticsCalculator.Calculate(
            Snapshot(visits, appointments, [Window(Day, 9, 13)]), Request(Day, Day)).Single().Hours;

        Assert.Equal(2, hours[9].ScheduledStarts);
        Assert.Equal(0, hours[10].ScheduledStarts); // Pending is not a scheduled start.
        Assert.Equal(20.0, hours[9].BusyMinutes, 6);
        Assert.Equal(1.0 / 3.0, hours[9].Utilization!.Value, 9);
    }

    // ---------- Doctor-level Little's check on an empty period ----------

    [Fact]
    public void A_doctor_with_no_open_time_has_no_rates_and_insufficient_data()
    {
        var metrics = MetricsFor(Snapshot(hours: []), Request(Day, Day));

        Assert.Null(metrics.LambdaPerHour);
        Assert.Null(metrics.Rho);
        Assert.Equal(TheoreticalStatus.InsufficientData, metrics.TheoreticalStatus);
        Assert.Equal("No open time in the period.", metrics.TheoreticalReason);
    }
}
