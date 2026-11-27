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

    private static AnalyticsVisitRow Booked(DateOnly day, int slotHour, int slotMinute, DateTimeOffset arrived, DateTimeOffset started, DateTimeOffset completed) =>
        new(DoctorId, Guid.NewGuid(), day, QueueEntrySource.Scheduled, QueueEntryStatus.Completed, arrived, started.AddMinutes(-1), started, completed, false,
            AppointmentDate: day, AppointmentStartTime: new TimeOnly(slotHour, slotMinute));

    private static AnalyticsVisitRow WalkInServed(DateOnly day, DateTimeOffset arrived, DateTimeOffset started, DateTimeOffset completed) =>
        new(DoctorId, Guid.NewGuid(), day, QueueEntrySource.WalkIn, QueueEntryStatus.Completed, arrived, started.AddMinutes(-1), started, completed, false);

    private static AnalyticsVisitRow NotServed(DateOnly day, DateTimeOffset arrived, QueueEntryStatus status, bool clinicUnavailable = false) =>
        new(DoctorId, Guid.NewGuid(), day, QueueEntrySource.WalkIn, status, arrived, null, null, null, clinicUnavailable);

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
    public void Mg1_flags_near_saturation_only_above_85_percent_with_computed_values()
    {
        // Mean service 20 min (1/3 h): lambda 2.58 gives rho 0.86, lambda 2.52 gives rho 0.84, lambda 3 gives rho 1.
        var above = MG1Model.Compute(2.58, 1.0 / 3.0, 1.0 / 36.0, 10);
        var below = MG1Model.Compute(2.52, 1.0 / 3.0, 1.0 / 36.0, 10);
        var overloaded = MG1Model.Compute(3.0, 1.0 / 3.0, 1.0 / 36.0, 10);

        Assert.Equal(TheoreticalStatus.Computed, above.Status);
        Assert.True(above.NearSaturation);
        Assert.False(below.NearSaturation);
        Assert.False(overloaded.NearSaturation);
    }

    [Fact]
    public void Kingman_with_random_arrivals_equals_pollaczek_khinchine()
    {
        // Ca^2 = 1 is Poisson arrivals: the same lambda 2/h, 20 +- 10 min case as the hand M/G/1 example (Wq = 5/12 h).
        var result = KingmanModel.Compute(2.0, 1.0 / 3.0, 1.0 / 36.0, arrivalCa2: 1.0);

        Assert.Equal(5.0 / 12.0, result!.Value.WqHours, 9);
        Assert.Equal(5.0 / 6.0, result.Value.Lq, 9);
    }

    [Fact]
    public void Kingman_with_evenly_spaced_arrivals_waits_less()
    {
        // Ca^2 = 0, Cs^2 = (1/36) / (1/9) = 1/4, rho = 2/3: Wq = (rho / (1 - rho)) * (0 + 1/4) / 2 * (1/3 h) = 1/12 h.
        var result = KingmanModel.Compute(2.0, 1.0 / 3.0, 1.0 / 36.0, arrivalCa2: 0.0);

        Assert.Equal(1.0 / 12.0, result!.Value.WqHours, 9);
        Assert.Equal(1.0 / 6.0, result.Value.Lq, 9);
        Assert.Null(KingmanModel.Compute(3.0, 1.0 / 3.0, 1.0 / 36.0, arrivalCa2: 0.5));
    }

    [Fact]
    public void Arrival_variability_and_kingman_wait_come_from_measured_gaps()
    {
        // Arrivals every 20 min (Ca^2 = 0); services 10, 20, 10, 20 (mean 15, sample variance 100/3); lambda 1/h, rho 1/4.
        // Kingman Wq = (1/3) * (0 + (100/3)/225) / 2 * 15 min = 10/27 min.
        var visits = new[]
        {
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 0), Local(Day, 9, 10)),
            Served(Day, Local(Day, 9, 20), Local(Day, 9, 20), Local(Day, 9, 40)),
            Served(Day, Local(Day, 9, 40), Local(Day, 9, 40), Local(Day, 9, 50)),
            Served(Day, Local(Day, 10, 0), Local(Day, 10, 0), Local(Day, 10, 20))
        };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)]), Request(Day, Day));

        Assert.Equal(3, metrics.InterarrivalSampleCount);
        Assert.Equal(0.0, metrics.ArrivalVariabilityCa2!.Value, 9);
        Assert.Equal(10.0 / 27.0, metrics.KingmanWqMinutes!.Value, 9);
        Assert.Equal(1.0 * (10.0 / 27.0) / 60.0, metrics.KingmanLq!.Value, 9);
    }

    [Fact]
    public void Interarrival_gaps_are_taken_within_each_day()
    {
        // Two days, two arrivals 20 min apart on each. The overnight gap is not an interarrival time.
        var nextDay = Day.AddDays(1);
        var visits = new[]
        {
            NotServed(Day, Local(Day, 9, 0), QueueEntryStatus.Cancelled),
            NotServed(Day, Local(Day, 9, 20), QueueEntryStatus.Cancelled),
            NotServed(nextDay, Local(nextDay, 9, 0), QueueEntryStatus.Cancelled),
            NotServed(nextDay, Local(nextDay, 9, 20), QueueEntryStatus.Cancelled)
        };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13), Window(nextDay, 9, 13)]), Request(Day, nextDay));

        Assert.Equal(2, metrics.InterarrivalSampleCount);
        Assert.Equal(0.0, metrics.ArrivalVariabilityCa2!.Value, 9);
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

    // ---------- Little's Law line, patients not seen, waits by source ----------

    [Fact]
    public void Little_law_line_uses_patients_seen_per_hour_like_the_check()
    {
        // T = 4 h. Two served (waits 0 and 10 min, in clinic 20 and 30 min) and two walk-ins cancelled before the doctor.
        var visits = new[]
        {
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 0), Local(Day, 9, 20)),
            Served(Day, Local(Day, 9, 10), Local(Day, 9, 20), Local(Day, 9, 40)),
            NotServed(Day, Local(Day, 10, 0), QueueEntryStatus.Cancelled),
            NotServed(Day, Local(Day, 11, 0), QueueEntryStatus.Cancelled)
        };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)]), Request(Day, Day));

        // Arrivals per hour still counts everyone who joined: 4 / 4 h.
        Assert.Equal(1.0, metrics.LambdaPerHour!.Value, 9);
        // Seen per hour = 2 / 4 h = 0.5. Lq line = 0.5 * (5/60) = 1/24; Ls line = 0.5 * (25/60) = 5/24.
        Assert.Equal(1.0 / 24.0, metrics.LittleLq!.Value, 9);
        Assert.Equal(5.0 / 24.0, metrics.LittleLs!.Value, 9);
        // Measured Lq = 10 patient-minutes / 240 min = 1/24, so the line and the measured value agree.
        Assert.Equal(1.0 / 24.0, metrics.MeasuredLq!.Value, 9);
        Assert.Equal(0.0, metrics.LittleLqCheckPercent!.Value, 6);
    }

    [Fact]
    public void Little_check_gap_is_the_share_of_waiting_outside_open_hours()
    {
        // Open 09:00-13:00. Two patients arrive at 08:30. Waits 30, 45, 5 and 5 min = 85 min; 60 of them before 09:00.
        var visits = new[]
        {
            Served(Day, Local(Day, 8, 30), Local(Day, 9, 0), Local(Day, 9, 15)),
            Served(Day, Local(Day, 8, 30), Local(Day, 9, 15), Local(Day, 9, 30)),
            Served(Day, Local(Day, 9, 40), Local(Day, 9, 45), Local(Day, 10, 0)),
            Served(Day, Local(Day, 10, 0), Local(Day, 10, 5), Local(Day, 10, 20))
        };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)]), Request(Day, Day));

        Assert.Equal(60.0 / 85.0 * 100.0, metrics.LittleLqCheckPercent!.Value, 6);
    }

    [Fact]
    public void Patients_who_left_or_were_cancelled_before_the_doctor_count_as_not_seen()
    {
        var visits = new[]
        {
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 0), Local(Day, 9, 20)),
            NotServed(Day, Local(Day, 9, 10), QueueEntryStatus.Cancelled),
            NotServed(Day, Local(Day, 9, 20), QueueEntryStatus.NoShow),
            NotServed(Day, Local(Day, 9, 30), QueueEntryStatus.Waiting),                        // still in the queue: not counted
            NotServed(Day, Local(Day, 9, 40), QueueEntryStatus.Cancelled, clinicUnavailable: true) // excluded from every measure
        };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)]), Request(Day, Day));

        Assert.Equal(4, metrics.ArrivalsCount);
        Assert.Equal(2, metrics.NotSeenCount);
        Assert.Equal(0.5, metrics.NotSeenRate!.Value, 9);
    }

    [Fact]
    public void Waits_are_reported_separately_for_booked_patients_and_walk_ins()
    {
        var visits = new[]
        {
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 10), Local(Day, 9, 30)),       // booked, wait 10
            Served(Day, Local(Day, 9, 20), Local(Day, 9, 30), Local(Day, 9, 50)),      // booked, wait 10
            WalkInServed(Day, Local(Day, 9, 0), Local(Day, 9, 50), Local(Day, 10, 10)) // walk-in, wait 50
        };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)]), Request(Day, Day));

        Assert.Equal(70.0 / 3.0, metrics.MeasuredWqMinutes!.Value, 9);
        Assert.Equal(2, metrics.ServedScheduledCount);
        Assert.Equal(10.0, metrics.MeasuredWqScheduledMinutes!.Value, 9);
        Assert.Equal(1, metrics.ServedWalkInCount);
        Assert.Equal(50.0, metrics.MeasuredWqWalkInMinutes!.Value, 9);
    }

    [Fact]
    public void Delay_past_the_appointment_time_ignores_waiting_from_an_early_arrival()
    {
        var visits = new[]
        {
            Booked(Day, 9, 20, Local(Day, 9, 0), Local(Day, 9, 30), Local(Day, 9, 45)),   // early: delay from 09:20 = 10
            Booked(Day, 9, 0, Local(Day, 9, 10), Local(Day, 9, 15), Local(Day, 9, 25)),   // late: delay from arrival = 5
            Booked(Day, 10, 0, Local(Day, 9, 30), Local(Day, 9, 50), Local(Day, 10, 5)),  // seen before the slot: 0
            Served(Day, Local(Day, 10, 10), Local(Day, 10, 20), Local(Day, 10, 30)),      // no appointment time: left out
            WalkInServed(Day, Local(Day, 10, 30), Local(Day, 11, 0), Local(Day, 11, 10))  // walk-in: left out
        };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)]), Request(Day, Day));

        Assert.Equal(3, metrics.DelayPastAppointmentCount);
        Assert.Equal(5.0, metrics.DelayPastAppointmentMinutes!.Value, 9);
    }

    [Fact]
    public void Arrivals_outside_open_time_are_counted()
    {
        // Open 09:00-13:00 with a break 11:00-11:30. Arrivals at 08:30, 11:10 and 13:30 are outside; 09:10 is inside.
        var visits = new[]
        {
            NotServed(Day, Local(Day, 8, 30), QueueEntryStatus.Waiting),
            NotServed(Day, Local(Day, 9, 10), QueueEntryStatus.Waiting),
            NotServed(Day, Local(Day, 11, 10), QueueEntryStatus.Waiting),
            NotServed(Day, Local(Day, 13, 30), QueueEntryStatus.Waiting)
        };
        var breaks = new[] { new AnalyticsBreakRow(DoctorId, Day, new TimeOnly(11, 0), new TimeOnly(11, 30)) };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)], breaks: breaks), Request(Day, Day));

        Assert.Equal(3, metrics.ArrivalsOutsideOpenTime);
    }

    [Fact]
    public void Long_consultations_count_toward_busy_time_at_the_cap()
    {
        // A 20-minute consultation and a 70-minute one (cap 60). The long one is left out of the mean but counts
        // 60 busy minutes: 09:30-10:30, so 30 in hour 9 and 30 in hour 10.
        var visits = new[]
        {
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 0), Local(Day, 9, 20)),
            Served(Day, Local(Day, 9, 30), Local(Day, 9, 30), Local(Day, 10, 40))
        };
        var result = AnalyticsCalculator.Calculate(Snapshot(visits, hours: [Window(Day, 9, 13)]), Request(Day, Day)).Single();

        Assert.Equal(20.0, result.Metrics.MeanServiceMinutes!.Value, 9);
        Assert.Equal(80.0 / 60.0, result.Metrics.MeasuredServiceHours!.Value, 9);
        Assert.Equal(1.0 / 3.0, result.Metrics.Utilization!.Value, 9);
        Assert.Equal(50.0, result.Hours[9].BusyMinutes, 6);
        Assert.Equal(30.0, result.Hours[10].BusyMinutes, 6);
    }

    [Fact]
    public void Near_saturation_is_reported_with_the_theoretical_values()
    {
        // One open hour, three 18-minute consultations: lambda = 3/h, mean 0.3 h, rho = 0.9.
        var visits = new[]
        {
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 0), Local(Day, 9, 18)),
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 18), Local(Day, 9, 36)),
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 36), Local(Day, 9, 54))
        };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 10)]), Request(Day, Day));

        Assert.Equal(TheoreticalStatus.Computed, metrics.TheoreticalStatus);
        Assert.True(metrics.TheoreticalNearSaturation);
    }

    // ---------- Open time includes the work the doctor actually did ----------

    [Fact]
    public void Open_time_includes_work_after_the_scheduled_end()
    {
        // Window 09:00-13:00 with no extension; ten 30-minute consultations back to back from 09:00 to 14:00.
        var visits = Enumerable.Range(0, 10)
            .Select(i => Served(Day, Local(Day, 9, 0).AddMinutes(30 * i), Local(Day, 9, 0).AddMinutes(30 * i), Local(Day, 9, 30).AddMinutes(30 * i)))
            .ToArray();
        var result = AnalyticsCalculator.Calculate(Snapshot(visits, hours: [Window(Day, 9, 13)]), Request(Day, Day)).Single();

        Assert.Equal(4.0, result.Metrics.ScheduledOpenHours, 9);
        Assert.Equal(5.0, result.Metrics.OpenHours, 9);
        Assert.Equal(2.0, result.Metrics.LambdaPerHour!.Value, 9);
        Assert.Equal(1.0, result.Metrics.Utilization!.Value, 9);
        Assert.Equal(60.0, result.Hours[13].OpenMinutes, 6);
    }

    [Fact]
    public void Open_time_includes_an_early_start_and_work_during_a_break()
    {
        // Window 09:00-13:00 with a break 11:00-11:30 (3.5 h scheduled). Consultations 08:40-09:00 and 11:05-11:25.
        var visits = new[]
        {
            Served(Day, Local(Day, 8, 40), Local(Day, 8, 40), Local(Day, 9, 0)),
            Served(Day, Local(Day, 11, 0), Local(Day, 11, 5), Local(Day, 11, 25))
        };
        var breaks = new[] { new AnalyticsBreakRow(DoctorId, Day, new TimeOnly(11, 0), new TimeOnly(11, 30)) };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)], breaks: breaks), Request(Day, Day));

        Assert.Equal(3.5, metrics.ScheduledOpenHours, 9);
        Assert.Equal(3.5 + 40.0 / 60.0, metrics.OpenHours, 9);
    }

    [Fact]
    public void Work_added_to_open_time_stops_at_the_cap()
    {
        // Window 09:00-10:00. A consultation from 09:30 that nobody closed until 12:00 adds only 10:00-10:30 (cap 60).
        var visits = new[] { Served(Day, Local(Day, 9, 30), Local(Day, 9, 30), Local(Day, 12, 0)) };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 10)]), Request(Day, Day));

        Assert.Equal(1.0, metrics.ScheduledOpenHours, 9);
        Assert.Equal(1.5, metrics.OpenHours, 9);
    }

    // ---------- Hourly workload ----------

    [Fact]
    public void Hourly_workload_compares_arrivals_in_each_hour_with_the_doctor_capacity()
    {
        // Window 09:00-11:00. Three arrivals in hour 9, one in hour 10; every consultation 20 min (1/3 h).
        // Hour 9: 3 arrivals per open hour * 1/3 h = 1.0; hour 10: 1/3; hour 8 has no open time.
        var visits = new[]
        {
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 0), Local(Day, 9, 20)),
            Served(Day, Local(Day, 9, 10), Local(Day, 9, 20), Local(Day, 9, 40)),
            Served(Day, Local(Day, 9, 30), Local(Day, 9, 40), Local(Day, 10, 0)),
            Served(Day, Local(Day, 10, 10), Local(Day, 10, 10), Local(Day, 10, 30))
        };
        var hours = AnalyticsCalculator.Calculate(Snapshot(visits, hours: [Window(Day, 9, 11)]), Request(Day, Day)).Single().Hours;

        Assert.Equal(1.0, hours[9].Workload!.Value, 9);
        Assert.Equal(1.0 / 3.0, hours[10].Workload!.Value, 9);
        Assert.Null(hours[8].Workload);
    }

    // ---------- Live: last hour and consultation length from history ----------

    [Fact]
    public void Live_arrivals_in_the_last_hour_are_reported_per_open_hour()
    {
        // Open 09:00-13:00, now 11:00. The last hour (10:00-11:00) has arrivals at 10:15 and 10:45; 09:30 is older.
        var visits = new[]
        {
            NotServed(Day, Local(Day, 9, 30), QueueEntryStatus.Waiting),
            NotServed(Day, Local(Day, 10, 15), QueueEntryStatus.Waiting),
            NotServed(Day, Local(Day, 10, 45), QueueEntryStatus.Waiting)
        };
        var snapshot = Snapshot(visits, hours: [Window(Day, 9, 13)]);

        Assert.Equal(2.0, MetricsFor(snapshot, Request(Day, Day, cutoff: Local(Day, 11, 0))).RecentArrivalsPerHour!.Value, 9);
        Assert.Null(MetricsFor(snapshot, Request(Day, Day)).RecentArrivalsPerHour);
    }

    [Fact]
    public void Live_consultation_mean_is_blended_with_history_until_today_has_enough()
    {
        // History: 40 consultations averaging 30 min (weight capped at 5). Today: one 20-minute consultation.
        // Blended mean = (20 + 5 * 30) / (1 + 5) = 170/6 min.
        var visits = new[] { Served(Day, Local(Day, 9, 0), Local(Day, 9, 0), Local(Day, 9, 20)) };
        var request = Request(Day, Day, cutoff: Local(Day, 11, 0)) with
        {
            ServicePriors = new Dictionary<Guid, ServicePrior> { [DoctorId] = new(30.0, 40) }
        };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)]), request);

        Assert.Equal(170.0 / 6.0, metrics.MeanServiceMinutes!.Value, 9);
        Assert.Equal(60.0 / (170.0 / 6.0), metrics.MuPerHour!.Value, 9);
        Assert.True(metrics.MeanServiceUsesHistory);
    }

    [Fact]
    public void Live_consultation_mean_comes_from_history_before_the_first_consultation()
    {
        var visits = new[] { NotServed(Day, Local(Day, 9, 30), QueueEntryStatus.Waiting) };
        var request = Request(Day, Day, cutoff: Local(Day, 11, 0)) with
        {
            ServicePriors = new Dictionary<Guid, ServicePrior> { [DoctorId] = new(30.0, 3) }
        };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)]), request);

        Assert.Equal(30.0, metrics.MeanServiceMinutes!.Value, 9);
        Assert.Equal(2.0, metrics.MuPerHour!.Value, 9);
        Assert.True(metrics.MeanServiceUsesHistory);
    }

    [Fact]
    public void Service_priors_use_valid_completed_consultations_below_the_cap()
    {
        var otherDoctor = Guid.NewGuid();
        var rows = new[]
        {
            new AnalyticsServiceHistoryRow(DoctorId, Day, Local(Day, 9, 0), Local(Day, 9, 20)),   // 20
            new AnalyticsServiceHistoryRow(DoctorId, Day, Local(Day, 10, 0), Local(Day, 10, 40)), // 40
            new AnalyticsServiceHistoryRow(DoctorId, Day, Local(Day, 11, 0), Local(Day, 12, 30)), // 90: at or above the cap
            new AnalyticsServiceHistoryRow(DoctorId, Day, Local(Day, 13, 0), Local(Day, 13, 0)),  // zero length: invalid
            new AnalyticsServiceHistoryRow(otherDoctor, Day, Local(Day, 9, 0), Local(Day, 9, 10))
        };

        var priors = AnalyticsCalculator.BuildServicePriors(rows, TimeSpan.FromMinutes(60));

        Assert.Equal(new ServicePrior(30.0, 2), priors[DoctorId]);
        Assert.Equal(new ServicePrior(10.0, 1), priors[otherDoctor]);
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

    // ---------- Past open time uses the schedule each day really had ----------

    private static readonly Guid WindowId = Guid.NewGuid();

    // Clinic-local Monday of the previous week: the same weekday as Day, so the window's DayOfWeek applies.
    private static readonly DateOnly PreviousWeek = Day.AddDays(-7);

    private static AnalyticsWorkingHourRow CurrentWindow(DateOnly day, int startHour, int endHour) =>
        new(DoctorId, day.DayOfWeek, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), WindowId);

    private static AnalyticsWorkingHourSnapshotRow Recorded(DateOnly effectiveFrom, int startHour, int endHour, bool active = true, DateTimeOffset? recordedAt = null) =>
        new(WindowId, DoctorId, Day.DayOfWeek, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), active, effectiveFrom,
            recordedAt ?? new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));

    private static double OpenHoursOn(DateOnly day, IReadOnlyList<AnalyticsWorkingHourRow> current, IReadOnlyList<AnalyticsWorkingHourSnapshotRow> snapshots) =>
        OpenTimeCalculator.TotalHours(OpenTimeCalculator.Build(day, day, ToUtc, current, [], [], [], cutoffUtc: null, snapshots));

    [Fact]
    public void A_window_changed_on_a_day_keeps_its_earlier_hours_before_that_day()
    {
        // Baseline 09:00-13:00 (4 h). From Day it is 09:00-11:00 (2 h). Only the current 09:00-11:00 row is active.
        var snapshots = new[] { Recorded(DateOnly.MinValue, 9, 13), Recorded(Day, 9, 11) };
        var current = new[] { CurrentWindow(Day, 9, 11) };

        Assert.Equal(4.0, OpenHoursOn(PreviousWeek, current, snapshots), 9);
        Assert.Equal(2.0, OpenHoursOn(Day, current, snapshots), 9);
    }

    [Fact]
    public void A_window_deactivated_on_a_day_is_closed_from_that_day_on()
    {
        // Baseline 09:00-13:00 active. From Day the window is inactive, so it is not in the active rows at all.
        var snapshots = new[] { Recorded(DateOnly.MinValue, 9, 13), Recorded(Day, 9, 13, active: false) };
        var current = Array.Empty<AnalyticsWorkingHourRow>();

        Assert.Equal(4.0, OpenHoursOn(PreviousWeek, current, snapshots), 9);
        Assert.Equal(0.0, OpenHoursOn(Day, current, snapshots), 9);
    }

    [Fact]
    public void Several_edits_on_one_day_the_latest_recording_wins()
    {
        // Two snapshots dated Day: 09:00-11:00 recorded at 08:00 UTC, then 09:00-10:00 recorded at 10:00 UTC. The list
        // deliberately holds the later one first, so the result cannot depend on list order. A week later the window
        // became 09:00-12:00, which is also the current row, so Day can only come out as 1 hour from its own snapshots.
        var snapshots = new[]
        {
            Recorded(DateOnly.MinValue, 9, 13),
            Recorded(Day, 9, 10, recordedAt: new DateTimeOffset(2026, 12, 7, 10, 0, 0, TimeSpan.Zero)),
            Recorded(Day, 9, 11, recordedAt: new DateTimeOffset(2026, 12, 7, 8, 0, 0, TimeSpan.Zero)),
            Recorded(Day.AddDays(7), 9, 12, recordedAt: new DateTimeOffset(2026, 12, 14, 8, 0, 0, TimeSpan.Zero))
        };
        var current = new[] { CurrentWindow(Day, 9, 12) };

        Assert.Equal(1.0, OpenHoursOn(Day, current, snapshots), 9);
    }

    [Fact]
    public void A_window_with_no_state_yet_on_a_day_is_closed_that_day()
    {
        // The first recorded state starts on Day, so the window did not exist the week before.
        var snapshots = new[] { Recorded(Day, 9, 11) };
        var current = new[] { CurrentWindow(Day, 9, 11) };

        Assert.Equal(0.0, OpenHoursOn(PreviousWeek, current, snapshots), 9);
        Assert.Equal(2.0, OpenHoursOn(Day, current, snapshots), 9);
    }

    [Fact]
    public void A_legacy_window_with_no_snapshots_keeps_its_current_hours_on_every_day()
    {
        // No snapshot rows: the current 09:00-13:00 row applies to both days, exactly as before snapshots existed.
        var current = new[] { Window(Day, 9, 13) };

        Assert.Equal(4.0, OpenHoursOn(PreviousWeek, current, []), 9);
        Assert.Equal(4.0, OpenHoursOn(Day, current, []), 9);
    }

    // ---------- Waiting room and time in clinic, including people who left ----------

    private static AnalyticsVisitRow Left(DateOnly day, DateTimeOffset arrived, QueueEntryStatus status, DateTimeOffset? closed) =>
        new(DoctorId, Guid.NewGuid(), day, QueueEntrySource.WalkIn, status, arrived, null, null, null, false, ClosedAtUtc: closed);

    [Fact]
    public void Waiting_room_and_time_in_clinic_count_people_who_left_while_they_were_there()
    {
        // T = 4 h. A served patient waited 10 minutes and was in clinic 30. A walk-in arrived 09:00 and left at 09:30
        // (30 minutes in the room). A cancellation with no recorded leave time is ignored by these measures.
        var visits = new[]
        {
            Served(Day, Local(Day, 9, 0), Local(Day, 9, 10), Local(Day, 9, 30)),
            Left(Day, Local(Day, 9, 0), QueueEntryStatus.Cancelled, Local(Day, 9, 30)),
            NotServed(Day, Local(Day, 10, 0), QueueEntryStatus.Cancelled)
        };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)]), Request(Day, Day));

        Assert.Equal(40.0 / 240.0, metrics.WaitingRoomAverage!.Value, 9);
        Assert.Equal(60.0 / 240.0, metrics.InClinicAverage!.Value, 9);
        // The queueing measures stay on served patients only.
        Assert.Equal(10.0 / 240.0, metrics.MeasuredLq!.Value, 9);
        Assert.Equal(1, metrics.LeftAfterWaitingCount);
        Assert.Equal(30.0, metrics.MeanWaitBeforeLeavingMinutes!.Value, 9);
    }

    [Fact]
    public void Patients_still_waiting_in_a_closed_period_are_not_in_the_waiting_room_average()
    {
        var visits = new[] { NotServed(Day, Local(Day, 10, 30), QueueEntryStatus.Waiting) };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)]), Request(Day, Day));

        Assert.Equal(0.0, metrics.WaitingRoomAverage!.Value, 9);
        Assert.Equal(0, metrics.LeftAfterWaitingCount);
        Assert.Null(metrics.MeanWaitBeforeLeavingMinutes);
    }

    [Fact]
    public void Live_waiting_room_counts_people_still_in_the_queue_up_to_now()
    {
        // Open 09:00-13:00, now 11:00, so T = 2 h. A patient who arrived at 10:30 is still waiting: 30 minutes in the room.
        var visits = new[] { NotServed(Day, Local(Day, 10, 30), QueueEntryStatus.Waiting) };
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)]), Request(Day, Day, cutoff: Local(Day, 11, 0)));

        Assert.Equal(2.0, metrics.OpenHours, 9);
        Assert.Equal(30.0 / 120.0, metrics.WaitingRoomAverage!.Value, 9);
        Assert.Equal(30.0 / 120.0, metrics.InClinicAverage!.Value, 9);
    }

    // ---------- Priority model: booked patients called before walk-ins ----------

    [Fact]
    public void Priority_waits_average_back_to_the_fcfs_wait_and_walk_ins_wait_longer()
    {
        // T = 4 h. Four booked patients and four walk-ins, each consultation exactly 20 minutes (variance 0). Each class
        // arrives at 1 per hour, so lambda = 2 per hour, rho = 2/3 and FCFS Wq = (lambda E[S^2] / 2) / (1 - rho) = 20 min.
        // With W0 = lambda E[S^2] / 2 = 1/9 h: booked Wq = (1/9) / (1 - 1/3) = 10 min; walk-in Wq = (1/9) / ((1 - 1/3)(1 - 2/3)) = 30 min.
        var visits = new List<AnalyticsVisitRow>();
        for (var i = 0; i < 4; i++)
        {
            var booked = Local(Day, 9, 0).AddMinutes(30 * i);
            visits.Add(Served(Day, booked, booked, booked.AddMinutes(20)));
            var walkIn = Local(Day, 9, 15).AddMinutes(30 * i);
            visits.Add(WalkInServed(Day, walkIn, walkIn, walkIn.AddMinutes(20)));
        }
        var metrics = MetricsFor(Snapshot(visits, hours: [Window(Day, 9, 13)]), Request(Day, Day));

        Assert.Equal(TheoreticalStatus.Computed, metrics.TheoreticalStatus);
        Assert.Equal(10.0, metrics.PriorityWqScheduledMinutes!.Value, 6);
        Assert.Equal(30.0, metrics.PriorityWqWalkInMinutes!.Value, 6);
        Assert.True(metrics.PriorityWqWalkInMinutes.Value > metrics.PriorityWqScheduledMinutes.Value);

        // Conservation law: the lambda-weighted mean of the two classes is the FCFS M/G/1 wait (both lambdas are 1 per hour).
        var lambdaScheduled = 4.0 / 4.0;
        var lambdaWalkIn = 4.0 / 4.0;
        var weightedMean = (lambdaScheduled * metrics.PriorityWqScheduledMinutes.Value + lambdaWalkIn * metrics.PriorityWqWalkInMinutes.Value)
                           / (lambdaScheduled + lambdaWalkIn);
        Assert.Equal(metrics.TheoreticalWqMinutes!.Value, weightedMean, 6);
    }
}
