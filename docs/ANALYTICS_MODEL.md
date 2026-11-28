# MediCare Queue Analytics Model

Status: implemented in Phase 4. Source of the model: `docs/TECHNICAL_SPECIFICATION.md` section 7 and the
"Analytics model" decision in `docs/DECISIONS.md`. Code: `MediCare.Application/Analytics/` (pure calculation) and
`MediCare.Application/Services/AnalyticsService.cs` (loading, caching, live and historical rules).

## 1. Scope and structure

- Each doctor is an independent single server. Clinic results are per-doctor rows plus totals. There is no clinic-level
  M/G/1 model, because doctors do not share a queue.
- Two views exist and must not be mixed:
  - **Historical**: a closed period that ends before the clinic-local today. Results may be cached for 5 minutes.
  - **Live**: today only, from the start of the day to "now". Live results are never cached and are labelled "LIVE".
- Time is clinic-local for dates and hours (Africa/Cairo, configured). Timestamps are stored in UTC.

## 2. Observed data

Every attendance attempt (queue entry) records the physical arrival and the scheduled-queue events:

| Symbol | Source |
|---|---|
| arrival `T_arr` | `QueueEntry.ArrivedAt` |
| call `T_call` | `QueueEntry.CalledAt` |
| start `T_start` | `QueueEntry.ConsultationStartedAt` |
| end `T_end` | `QueueEntry.CompletedAt` |

Service time `S = T_end - T_start`.

## 3. Observed open time T

`T` is the total time the doctor was open in the period:

1. For each clinic-local date in the period, take the doctor's weekly working windows for that weekday, as they were in
   force on that date (phase 3, see the paragraph after step 6):
   - A window **with** snapshot history uses the snapshot in force on that date: the greatest `EffectiveFrom` on or
     before the date, with ties broken by the latest `RecordedAtUtc`. The snapshot counts only if it is **active** and on
     that weekday. Before a window's first snapshot it adds nothing.
   - A window **with no snapshot** (legacy) keeps the old behavior: its current state applies to all dates, and an
     inactive one is ignored.
2. An **Approved** working-hour extension replaces the end of the window it was recorded against (the same rule as
   DoctorAvailabilityService). Pending and rejected extensions add nothing.
3. Subtract the dated breaks of that date (recurring breaks are not supported).
4. Skip holiday dates.
5. For live results, clip every interval to "now".
6. **Work outside the schedule** (`OpenTimeCalculator.IncludeWork`). The doctor was open for work that the consultation
   records show, even outside the schedule:
   - Every consultation interval (`ConsultationStartedAt` to `CompletedAt`) adds its time, counted up to the cap. A
     consultation that nobody closed therefore adds at most the cap.
   - On each clinic-local day with consultations, the stretch from the first consultation start to the scheduled start,
     and from the scheduled end to the last consultation end. A day with consultations but no scheduled hours adds the
     span from the first start to the last end.
   - For live results these intervals are clipped to "now" as well.

`ScheduledOpenHours` is the schedule-only value (steps 1 to 5). `OpenHours` is the actual `T` (steps 1 to 6), and it is
the `T` used for λ, the busy share and the hourly profile. The doctor page shows how many hours were work outside the
schedule. Effect: overtime no longer pushes utilization above 100% (the review's case 4 went from 125% to 100%). A day
the doctor worked straight through still shows ρ ≈ 1 and **Overloaded**, even with nobody waiting, because T now holds
that busy time; whether such a day should count as overload is an open decision. The day-edge rule adds the whole gap:
one consultation at 07:00 on a 09:00 schedule adds 07:00-09:00 to T, which lowers λ and the busy share for that day.
This is a deliberate simplification (the doctor was demonstrably at work from the first consultation).

**Working-hour history (phase 3).** The schedule is effective-dated by an append-only table, `DoctorWorkingHourSnapshots`
(`WorkingHourId`, `DoctorId`, `DayOfWeek`, `StartTime`, `EndTime`, `IsActive`, `EffectiveFrom` as a clinic-local date,
`RecordedAtUtc`). The table was added instead of changing `DoctorWorkingHours`, so that table keeps its unique index and
the row identity that the rest of the schedule relies on. `DoctorScheduleService` writes a snapshot on each successful add,
time change, deactivation and reactivation. It is effective from the clinic's today and is written in the same transaction,
and only after every check passes. The first change to a legacy window (one with no snapshot yet, for example a window
made by the demo seeder) also writes a baseline of its old state, effective from `DateOnly.MinValue`. Limitation: history
starts when this is deployed (see section 12). For a legacy window that was deactivated before then, the past is still
unknown.

`T` is in hours. Daylight-saving days are handled by converting each local window time with the clinic clock, so a
spring-forward day gives the real open duration (not a naive 24-hour assumption).

## 4. Measured metrics (authoritative)

Exclusions are applied first and reported with counts:

- **Clinic unavailable.** Attempts tied to an appointment cancelled with reason `clinic unavailable` are excluded from
  every measure (arrivals, service, waiting, no-show, hourly profile). Reported as `ExcludedClinicUnavailable`.
- **Invalid timestamps.** A Completed attempt is served only when `ArrivedAt <= ConsultationStartedAt < CompletedAt`.
  Others are reported as `ExcludedInvalidTimestamps`.
- **Service cap.** The service sample (mean, variance, μ) keeps only `0 < S < cap`. The cap is the named constant
  `ExpectedDurationOptions.DefaultMaxDurationMinutesCap` (60 minutes, configurable through the same options). Excluded
  observations are reported as `ExcludedServiceAtOrAboveCap`. `0 < S` always holds for served observations. Busy time
  (busy share, hourly busy minutes) still counts every served consultation, a long one as `min(S, cap)`, so a real
  long consultation does not disappear from the doctor's workload.

Definitions:

| Metric | Definition |
|---|---|
| Arrivals N | Attempts (queue entries) in the period after the exclusions above |
| Served n | Completed attempts with valid timestamps |
| λ (arrival rate) | N / T, patients per hour |
| Mean service | mean of the service sample, in minutes |
| σ² | sample variance of the service sample (n − 1 denominator), in minutes squared |
| μ (service rate) | 1 / mean service time, per hour. **μ is the service rate, not the grace multiplier.** |
| ρ (utilization) | λ / μ |
| Measured Wq | mean of `started − arrived` over served attempts |
| Measured Wq, booked / walk-in | the same mean for Scheduled and for WalkIn attempts separately (booked patients are called first) |
| Delay past the booked time | Scheduled attempts with an appointment: mean of `max(0, started − max(arrived, booked start))`; waiting after arriving early is not counted |
| Measured Ws | mean of `completed − arrived` over served attempts |
| Measured Lq | time-average number waiting (arrived, not yet started), integrated over T by an event sweep, divided by T |
| Measured Ls | time-average number in the clinic (arrived, not yet completed), integrated over T, divided by T |
| Busy share (utilization of time) | `Σ min(S, cap)` over served attempts / T |
| Not seen | attempts that ended Cancelled or NoShow (joined the queue, never reached a consultation), and their share of N. Waiting, Called and InConsultation attempts are still in the queue and are not counted |
| Arrivals outside open time | attempts whose `ArrivedAt` is not inside T (before opening, in a break, after closing) |

Little's Law check: `(n/T)·Wq` and `(n/T)·Ws` against the measured time averages, all over served attempts. The
displayed line and the percentage use the same served throughput `n/T`. λ stays `N/T` everywhere else (workload, M/G/1),
because it is the demand on the doctor and must show a growing queue on the live board before those patients are served.

Both sides of the check come from the same timestamps, so it does **not** test whether the desk recorded times
correctly. On a closed timeline they are equal. The gap is exactly the share of W that lies outside T: waiting before
opening, during a break, or care after closing. The page says so and shows the number of arrivals outside open time.

Phase 3 leaves this Little's Law pair **unchanged**: it still uses served patients only, so its gap still equals the
waiting outside open hours. The waiting-room and in-clinic averages below count people who left, and they are a separate view.

### 4.1 Waiting room, time in clinic and leavers (phase 3)

Leavers are attempts that ended **Cancelled** or **NoShow** with a recorded `QueueEntries.ClosedAtUtc` later than their
arrival. `ClosedAtUtc` is set when reception cancels a Waiting entry, when a waiting patient's appointment is cancelled or
rescheduled, when a disruption cancels a waiting walk-in, and when a doctor is deactivated. It is **null** (and such an
attempt is left out of the metrics below) for entries closed as stale by the maintenance job, because that job runs on a
later day and the real leave time is unknown, and for all entries recorded before deployment. Those omitted attempts
understate these averages on days where they occur.

| Metric | Definition |
|---|---|
| `WaitingRoomAverage` | Time-average number in the waiting room over T, divided by T. Served patients count from arrival to the start of their consultation. Leavers count from arrival to `ClosedAtUtc`. On the live board, people still Waiting or Called count up to now. |
| `InClinicAverage` | The same integral, divided by T, with the stays running longer. Served patients count from arrival to the end of the consultation. Leavers count to `ClosedAtUtc`. On the live board, people still Waiting, Called or InConsultation count up to now. |
| `LeftAfterWaitingCount` | Number of leavers (the count above). |
| `MeanWaitBeforeLeavingMinutes` | Mean of `ClosedAtUtc − arrived` over the leavers, in minutes. Null when there are none. |

The Little's Law pair (`MeasuredLq`, `MeasuredLs`, `LittleLq`, `LittleLs` and their checks) is not part of this section and is
not changed by it (see section 4 above).

Live results additionally show: waiting now (status Waiting), called now (Called), in consultation (InConsultation),
the longest current wait of a Waiting attempt, and λ, μ, ρ measured so far today.

Two live rules were added in review phase 2:

- **Last hour**: arrivals in the last 60 minutes ÷ open hours in that same window. It is shown only when at least 15
  minutes of the last hour were open. λ from the start of the day is kept next to it.
- **Mean consultation with a 30-day prior.** The mean of today's consultations alone is unstable early in the day, so the
  live mean is blended with the doctor's valid consultations of the last 30 days (0 < S < cap, Completed, not
  `clinic unavailable`):

  `mean = (sum of today's S + w × history mean) ÷ (today's count + w)`, with `w = min(5, history count)`
  (`AnalyticsCalculator.PriorWeight` = 5).

  μ and the live workload are therefore available from the first arrival of the day. When the history is used, the flag
  `MeanServiceUsesHistory` is set. The history is loaded with one query (`IAnalyticsRepository.GetServiceHistoryAsync`).

## 5. Theoretical M/G/1 (reference baseline only)

Pollaczek-Khinchine formula, from the measured arrival rate and the measured service mean and variance (hours):

- Lq = (λ²σ² + ρ²) / (2(1 − ρ))
- Wq = Lq / λ
- Ws = Wq + 1/μ
- Ls = Lq + ρ

Rules:

- Shown **only when ρ < 1**. When ρ ≥ 1 the status is **Overloaded** and no theoretical numbers are shown (the
  steady state does not exist).
- **InsufficientData** when fewer than two service observations exist (the variance is undefined), when no open time
  exists, or when the service sample is empty. The reason is shown.
- **Near saturation** when `0.85 < ρ < 1` (`MG1Model.NearSaturationRho`): the values are shown with a warning. At this
  load the steady state takes far longer than one clinic day to reach, and a day starts with an empty queue, so the
  steady-state values are higher than a real day shows.
- The theoretical values are a reference baseline. M/G/1 assumes Poisson arrivals with independent service times. A
  scheduled clinic does not have Poisson arrivals (appointments cluster at the start of each slot), so measured values
  are authoritative and the theoretical values can differ from them.

### 5.1 Kingman G/G/1 (second reference)

Kingman's approximation allows general arrival variability, so it is shown next to M/G/1 as a second reference:

- Wq ≈ (ρ / (1 − ρ)) × ((Ca² + Cs²) / 2) × E[S]
- Lq = λ × Wq

Here `E[S]` is the mean service time, `Cs² = σ² / E[S]²` is the squared coefficient of variation of service, and `Ca²` is
the squared coefficient of variation of the arrival gaps:

- `Ca² = sample variance ÷ mean²` of the gaps between consecutive arrivals on the same clinic-local day. Overnight gaps
  (between days) are excluded. Needs at least 2 gaps.

With `Ca² = 1` the formula equals Pollaczek-Khinchine exactly, so Kingman generalizes M/G/1 rather than replacing it.

Rules:

- Shown only when the M/G/1 values are computed (`ρ < 1`) and `Ca²` is available (at least 2 gaps).
- Metrics: `ArrivalVariabilityCa2`, `InterarrivalSampleCount`, `KingmanWqMinutes`, `KingmanLq`.
- Why it is added: booked arrivals are more regular than random (`Ca² < 1`), while M/G/1 assumes `Ca² = 1`. For a
  scheduled clinic M/G/1 therefore overstates waits. In the simulation in `docs/QUEUEING_MODEL_REVIEW.md` (section 3.4,
  `Ca² = 0.61`), the measured mean wait was 18.3 minutes, M/G/1 gave 35.5 and Kingman gave 24.3. Measured values stay
  authoritative.

### 5.2 Priority model (phase 3)

Booked patients are called before walk-ins, so a single M/G/1 average hides two queues. The priority model is Cobham's
non-preemptive priority M/G/1: a consultation in progress is never interrupted, and both classes have the same service
distribution (the same sample mean `m` and variance `σ²` as M/G/1).

- `λ1 = booked attempts ÷ T`, `λ2 = walk-in attempts ÷ T`, `λ = λ1 + λ2`
- `ρ1 = λ1 ÷ μ`, `ρ = λ ÷ μ`
- `E[S²] = σ² + m²`
- `W0 = λ · E[S²] ÷ 2` (the mean work left by the consultation in progress)
- `Wq(booked) = W0 ÷ (1 − ρ1)`
- `Wq(walk-in) = W0 ÷ ((1 − ρ1)(1 − ρ))`, which is the same as `W0 ÷ ((1 − ρ1)(1 − ρ1 − ρ2))`

Rules:

- Shown on the doctor page next to M/G/1 and Kingman, and only when the M/G/1 values are computed (`ρ < 1`).
- Metrics: `PriorityWqScheduledMinutes` and `PriorityWqWalkInMinutes`.
- The λ-weighted average of the two waits equals the M/G/1 `Wq` (Kleinrock's conservation law). The tests check this.
- It is a reference, like M/G/1 and Kingman. It has the same assumptions (Poisson arrivals, independent service), so the
  measured per-class waits (section 4) stay authoritative.

## 6. Other KPIs

- **No-show rate** = NoShow / (Completed + NoShow) for appointments whose date is in the period and not after today,
  excluding `clinic unavailable` cancellations. Only appointments with a final attendance outcome count: Pending
  requests that were never confirmed, and Confirmed appointments that have not resolved yet, are not in the denominator.
- **Walk-in throughput** = completed walk-in attempts in the period, and per hour of T.
- **Not seen** = attempts that ended Cancelled or NoShow, as a count and a share of arrivals (also a column of the
  clinic table).
- **Queue length now** (live) = Waiting attempts.

## 7. Hourly profile

For each clinic-local hour of the day (0 to 23), summed over the period:

- **Scheduled starts**: Confirmed, Completed and NoShow appointments by their start hour.
- **Actual arrivals**: attempts by the clinic-local hour of `ArrivedAt`.
- **Busy minutes**: served consultations from their start, a long one only up to the cap, attributed by the minute:
  each one-minute segment is assigned to the local hour of the segment start. The attribution can be off by at most
  one minute at an hour edge.
- **Open minutes**: open time T per hour (same segmentation), including the work outside the schedule from section 3.
- **Utilization per hour** = busy / open.
- **Workload per hour** = (actual arrivals in the hour ÷ open hours in the hour) × mean consultation (hours). Cells at or
  above 0.85 are shown in red. This is the per-hour version of ρ, so a busy part of the day is visible even when the
  daily ρ is below 1.

Hours are computed from instants with the clinic time zone, so the spring-forward and fall-back days are correct.

## 8. Caching and reads

- One query per table per period and doctor set (queue entries, appointments, working hours, working-hour snapshots,
  holidays, approved extensions, breaks). Projections are AsNoTracking.
- Historical doctor and clinic results are cached in memory for 5 minutes, keyed by doctor set and period.
- Live results are never cached.
- Index `IX_QueueEntries_QueueDate_DoctorId` (migration `AddAnalyticsIndexes`) covers the date-range reads.

## 9. Access

- Admin: full clinic (historical table, doctor drill-down).
- Receptionist and Admin: live operations board.
- Doctor: own statistics only. The server resolves the doctor from the signed-in user; no doctor id is accepted from the
  form.

## 10. Known limitations

- Shown numbers depend on data quality: stale attempts are closed by the maintenance job; long consultations (≥ cap)
  are excluded from service statistics.
- Units: waiting and service times in minutes; λ and μ in patients (or services) per hour; Lq and Ls in patients.
- Cached historical numbers may lag by up to 5 minutes after a correction.
- Walk-in queue estimates (section 11) count booked patients who have not arrived yet as if all of them will come. There
  is no no-show weighting, so a walk-in's estimate is later than needed when some of them do not come.
- **Past open time follows the schedule in force each day only from deployment (phase 3).** Schedule history is recorded
  in `DoctorWorkingHourSnapshots` from the moment it is deployed (section 3). Earlier history is not reconstructed. For a
  legacy window (no snapshot) that was deactivated before deployment, the past is still unknown.
- **`QueueEntries.ClosedAtUtc` is unknown (null) for entries closed as stale by the maintenance job and for all entries
  recorded before deployment.** Those attempts are left out of `WaitingRoomAverage`, `InClinicAverage` and
  `LeftAfterWaitingCount` (section 4.1), so the averages do not include the time they spent waiting.

## 11. Queue board estimates

The queue board (`QueueBoardService.AssignEstimates`) shows when each patient is likely to start. Three rules were added
in review phase 2:

- **(a) Booked patients who have not arrived yet.** Booked patients in state `NotArrived` or `GraceRunning` are counted as
  coming. They are called before walk-ins once their slot comes, so each waiting walk-in's estimate moves past every such
  slot at or before it (a fixed-point count). `GraceExpired` patients are not counted.
- **(b) Range.** Every estimate has an 80% range of ±1.28 × σ × √m minutes, where:
  - `m` = patients ahead, plus 1 if someone is `Called` or in consultation;
  - `σ` = standard deviation of the doctor's last-30-day consultations (`ExpectedDurationResult.StdDevMinutes`).

  No range is shown when the historical average is not used. The earliest end is never before now. The pages show it as
  "around 10:40 (10:28–10:53)".
- **(c) Consultation in progress.** At any elapsed time t (not only after the expected length), it is expected to end at
  now + the mean remaining time (S − t) of past consultations longer than t. This needs at least 2 such consultations.
  Otherwise the estimate keeps the earlier rule: `max(now, start + expected length)`.

## 12. Deployment (phase 3)

- Migration `20261127224403_AddQueueEntryClosedAtAndWorkingHourSnapshots` is additive only: one nullable column
  (`QueueEntries.ClosedAtUtc`) and one new table (`DoctorWorkingHourSnapshots`), with FKs to `DoctorWorkingHours` and
  `Doctors` (both Restrict), and indexes on `(DoctorId, EffectiveFrom)` and `WorkingHourId`.
- The app does not migrate on startup. Apply `artifacts/AddQueueEntryClosedAtAndWorkingHourSnapshots.sql` (idempotent) to
  each database, local and live, **before** the new code runs against it.
- As of 2026-11-28 the script has not been applied to any database.

## Slot-length advice

Historical analytics (not live) compare a doctor's booking slot with the measured mean consultation (the service sample,
`0 < S < cap`). The suggested slot is that mean rounded to the nearest 5 minutes (half up, minimum 5). The doctor page
shows an alert, and the clinic table a "Check slot" badge, when the suggestion differs from the current slot and the
period has at least `SlotLengthAdvice.MinimumSample` (20) consultations. It is advice only; the admin or the doctor changes
the appointment duration on the schedule page. Rationale: `docs/analysis/slot_length_simulation.py`. With the same demand,
a slot within a couple of minutes of the mean changes waits by well under a minute, while a slot clearly shorter than the
mean adds overtime (simulated, Dr Khaled Nabil: 30-minute slot, mean ≈ 33 min).
