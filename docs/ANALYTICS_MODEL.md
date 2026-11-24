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

1. For each clinic-local date in the period, take the doctor's **active** weekly working windows for that weekday.
2. An **Approved** working-hour extension replaces the end of the window it was recorded against (the same rule as
   DoctorAvailabilityService). Pending and rejected extensions add nothing.
3. Subtract the dated breaks of that date (recurring breaks are not supported).
4. Skip holiday dates.
5. For live results, clip every interval to "now".

`T` is in hours. Daylight-saving days are handled by converting each local window time with the clinic clock, so a
spring-forward day gives the real open duration (not a naive 24-hour assumption).

## 4. Measured metrics (authoritative)

Exclusions are applied first and reported with counts:

- **Clinic unavailable.** Attempts tied to an appointment cancelled with reason `clinic unavailable` are excluded from
  every measure (arrivals, service, waiting, no-show, hourly profile). Reported as `ExcludedClinicUnavailable`.
- **Invalid timestamps.** A Completed attempt is served only when `ArrivedAt <= ConsultationStartedAt < CompletedAt`.
  Others are reported as `ExcludedInvalidTimestamps`.
- **Service cap.** The service sample keeps only `0 < S < cap`. The cap is the named constant
  `ExpectedDurationOptions.DefaultMaxDurationMinutesCap` (60 minutes, configurable through the same options). Excluded
  observations are reported as `ExcludedServiceAtOrAboveCap`. `0 < S` always holds for served observations.

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
| Measured Ws | mean of `completed − arrived` over served attempts |
| Measured Lq | time-average number waiting (arrived, not yet started), integrated over T by an event sweep, divided by T |
| Measured Ls | time-average number in the clinic (arrived, not yet completed), integrated over T, divided by T |
| Busy share (utilization of time) | total service time (service sample) / T |

Little's Law check: `λ·Wq` and `λ·Ws` are shown with `λ = N/T`. The percentage difference against the time averages uses
the served throughput `n/T`, because the time averages and W are both taken over served attempts. Both are equal
on a closed timeline (every arrival is served and every sojourn lies inside T). Boundary effects (a patient still
waiting when the clinic closes) appear as a difference and are expected.

Live results additionally show: waiting now (status Waiting), called now (Called), in consultation (InConsultation),
the longest current wait of a Waiting attempt, and λ, μ, ρ measured so far today.

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
- The theoretical values are a reference baseline. M/G/1 assumes Poisson arrivals with independent service times. A
  scheduled clinic does not have Poisson arrivals (appointments cluster at the start of each slot), so measured values
  are authoritative and the theoretical values can differ from them.

## 6. Other KPIs

- **No-show rate** = NoShow / (Completed + NoShow) for appointments whose date is in the period and not after today,
  excluding `clinic unavailable` cancellations. Only appointments with a final attendance outcome count: Pending
  requests that were never confirmed, and Confirmed appointments that have not resolved yet, are not in the denominator.
- **Walk-in throughput** = completed walk-in attempts in the period, and per hour of T.
- **Queue length now** (live) = Waiting attempts.

## 7. Hourly profile

For each clinic-local hour of the day (0 to 23), summed over the period:

- **Scheduled starts**: Confirmed, Completed and NoShow appointments by their start hour.
- **Actual arrivals**: attempts by the clinic-local hour of `ArrivedAt`.
- **Busy minutes**: service-sample consultations, attributed by the minute: each one-minute segment is assigned to the
  local hour of the segment start. The attribution can be off by at most one minute at an hour edge.
- **Open minutes**: open time T per hour (same segmentation).
- **Utilization per hour** = busy / open.

Hours are computed from instants with the clinic time zone, so the spring-forward and fall-back days are correct.

## 8. Caching and reads

- One query per table per period and doctor set (queue entries, appointments, working hours, holidays, approved
  extensions, breaks). Projections are AsNoTracking.
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
