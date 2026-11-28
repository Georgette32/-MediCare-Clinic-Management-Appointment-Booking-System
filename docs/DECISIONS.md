# MediCare Decisions

## Confirmed business rules

### Appointment booking and schedule

- Patient booking requests remain Pending until Reception confirms or rejects them.
- Double booking is prohibited. A filtered unique index covers active appointment slots per doctor/date/start; the transactional overlap check also rejects partial overlaps. Availability must respect a doctor's working hours, breaks, and holidays.
- Appointment duration is configured per doctor. Exact duration values remain configurable; do not hardcode a value or default.
- Slot generation uses configured appointment duration. Expected duration is used for estimates only; do not mix these concepts.
- Patients need an account to book. The patient can create the account, or Reception can create it. Walk-ins are registered by Reception, which creates an account if one does not already exist. Guest access is not allowed.
- Admin provisions staff accounts and links Doctor profiles to their accounts. Receptionist remains a distinct primary role, separate from Admin.

### Queue and attendance

- Create one QueueEntry per attendance attempt. `AppointmentId` is nullable: a scheduled attendance attempt links to its appointment; a walk-in attendance attempt has no appointment link. A later attendance attempt, including a walk-in after a No-Show, creates a separate QueueEntry.
- A No-Show appointment remains in history. The patient's later arrival creates a separate walk-in QueueEntry with no appointment link.
- Scheduled patients are ordered by appointment time. Walk-ins are ordered FIFO by actual `ArrivedAt`; `QueueSequence` is walk-in-only and is a deterministic tie-breaker. Scheduled entries have no sequence. Never combine these orderings.
- Grace Period begins at the scheduled appointment time when the patient has not checked in. The patient retains scheduled priority. If an approved replacement consultation is already in progress when they arrive within the Grace Period, they wait until that consultation finishes.
- Expected consultation duration is the per-doctor average over the trailing 30 clinic-local days. Count only Completed consultations with valid timestamps and durations satisfying `0 < duration < 60 minutes`.
- The minimum consultation sample size is configurable and has no hardcoded value or default. If it is unset or the sample is below the threshold, use that doctor's configured appointment duration for both timeline estimates and Grace Period calculation.
- The 60-minute consultation-duration cap must be represented as a named constant or setting, not a scattered literal.
- Grace Period is per doctor and is three times expected consultation duration. Freeze the calculated Grace Period on the appointment when its timer starts; do not recalculate it live.
- Anchor `GracePeriodStartedAtUtc` to the appointment's scheduled local date and start time, converted to UTC by `IClinicClock`; a late expiry-processing run must not shift the anchor. The grace period expires inclusively when `currentUtc >= scheduledStartUtc + frozenGracePeriodMinutes`.
- Resolve an ambiguous clinic-local scheduled time during a fall-back transition using standard time. Resolve a nonexistent local time during a spring-forward gap to the first valid local instant after the gap. These rules keep scheduled-time conversion deterministic and prevent a DST gap from making an appointment's expiry processing fail.
- Every walk-in admission request has a separate approval record bound to exactly one QueueEntry and its Doctor; no global or reusable approval exists. Reception/Admin submit requests, and only the Doctor linked to that doctor profile decides. Reception/Admin cannot decide. Rejection preserves the Waiting entry and FIFO position; a later request is allowed. At most one Pending request exists per entry. Approval is a permit record only: a separate Reception/Admin Call operation revalidates and consumes it exactly once before moving a WalkIn entry from Waiting to Called.
- Pending requests expire after a configurable positive duration (default 30 minutes), capped at the end of the clinic day. Approved records are valid only on the same clinic day. Expiry is evaluated at read/call time and recorded as Expired; records remain for history.
- The admission approval table check constraint permits only `Pending`, `Approved`, `Rejected`, and `Expired`: Pending requires null `DecisionAtUtc` and `UsedAtUtc`; Approved requires non-null `DecisionAtUtc` and may have null or set `UsedAtUtc`; Rejected and Expired require non-null `DecisionAtUtc` and null `UsedAtUtc`; when both decision and use timestamps are set, use must not precede decision. Expiration is evaluated at read/call time, so an Approved row whose expiry time has passed may remain Approved in the database until swept; Call must still reject it as expired.
- A doctor may approve a walk-in while another patient is InConsultation; approval alone does not call or admit the entry. The separate Call operation requires the doctor to have no Called or InConsultation entry and checks normal configured working hours only. Working-hour extensions are not integrated until that feature exists.
- Call Next is not implemented yet. Its confirmed priority is scheduled patients first in scheduled-time order, followed by approved walk-ins in FIFO order; completing it closes the remaining Scenario A / walk-in replacement queue item.
- A WalkIn cannot be called ahead of an arrived, checked-in scheduled patient waiting for that doctor. A scheduled patient whose grace window remains open but has not checked in may be replaced by an approved walk-in. Calling the replacement must not change the original appointment or its frozen Grace Period values; if the scheduled patient checks in within grace, their scheduled priority is preserved after the replacement consultation finishes.
- The Doctor sees pending requests through a refreshable list; no polling or real-time channel is used.
- An extension of working hours requires explicit Doctor approval and must be recorded before the extra availability is offered.
- Walk-ins may wait in FIFO order or receive an alternative appointment after the last booked slot. Required Doctor approvals apply, including approval of an extension when needed.

### Cancellation, rescheduling, timezone, and timestamps

- Patients may cancel only before the appointment's calendar day. Reception and Admin may cancel on the same day.
- A patient's reschedule request follows the same before-the-appointment-day cutoff.
- The appointment day is determined by comparing its date with the current clinic-local date. It begins at 00:00 Africa/Cairo; do not create a midnight instant because of DST transitions.
- A patient reschedule request creates a linked Pending replacement while the original remains Confirmed and reserves its slot. Only when the replacement is confirmed, mark the original Rescheduled in the same transaction. Rejected/cancelled replacements leave the original unchanged.
- A replacement using the same Doctor, appointment date, and start time as its linked original is rejected as not being a real reschedule. A different Doctor, date, or start time is allowed if available; the original stays Confirmed until replacement confirmation.
- Ignore the linked original appointment during replacement overlap checks. Enforce one live (Pending or Confirmed) replacement per original; Cancelled/Rejected attempts do not block a new attempt.
- Reception/Admin handle affected patients by proposing available replacement slots. A replacement booked by Reception/Admin is created directly as Confirmed and the original becomes Rescheduled in one transaction. If no replacement slot exists, cancel the original with a reason.
- Store timestamps in UTC. Clinic timezone is configurable, with no code default assumed. The deployment configuration value is `Africa/Cairo`.
- Use a real timezone database with DST support and handle Windows versus IANA timezone IDs.
- Add tests for calendar-day boundaries and a DST transition.
- Analytics `μ` denotes service rate; it is not the Grace Period multiplier.

### Schema decisions

- Doctor specialization is a required FK to `Specializations` (`Id`, unique `Name`, optional `Description`); deleting a referenced specialization is restricted.
- Persisted checks enforce appointment, break, and working-hour end times after start times, and holiday end dates on/after start dates.
- Identity user `FullName` is limited to 200 characters, matching Doctor and Patient profile names.

### Step 2B — account provisioning and first-login security

- Admin creates Doctor and Receptionist staff accounts; there is no public staff or Doctor registration. An Admin-created Doctor Identity user and Doctor profile are persisted in one transaction. Admin supplies a required positive appointment duration; there is no default.
- Reception creates Patient accounts. Accept Egyptian mobile numbers only. Normalize accepted numbers to canonical `+20` form and use that value as the Identity username. Enforce uniqueness on Identity username and Patient phone number. Document and test normalization and invalid-number handling.
- Patients may also register their own accounts with a required Egyptian mobile number, optional email, and password chosen by the patient. Self-registration creates the Patient Identity user and profile in one transaction, assigns the Patient role, and does not set `MustChangePassword`.
- Temporary passwords are generated randomly by the system and shown once to the staff member creating the account. Staff do not choose them.
- Persist `ApplicationUser.MustChangePassword` and redirect authenticated users who have it set to the password-change page until they change their password. Apply this to Reception-created Patients and Admin-created staff accounts.
- Bootstrap the initial Admin from secure configuration (User Secrets or environment variables). Refuse bootstrap when the required configuration is missing. There must be no default Admin email or password in the repository. Do not run `Database.MigrateAsync` at application startup.
- Required bootstrap keys are `BootstrapAdmin:Email`, `BootstrapAdmin:Password`, and `BootstrapAdmin:FullName` (environment variables use the double-underscore form). In local development, provide them with .NET User Secrets; never commit their values.
- Add a separate migration for the account-security/schema additions and generate its SQL script for review only. Never edit or apply `InitialCreate` for this work.
- Accepted input formats: Egyptian local mobile (`01` followed by operator prefix `0`, `1`, `2`, or `5` and eight subscriber digits), international `+20` form, and `0020` international prefix. Permit ASCII digits, a leading `+` for the `+20` form, and ignore spaces, hyphens, and parentheses. Store `+20` plus the 10-digit national significant number. Reject malformed, unsupported, non-Egyptian, or landline numbers before account creation.
- A bare `20` country-code prefix without `+` or `00` is rejected; do not infer or silently accept it as an international number.
- Any future Admin password-reset flow must set `ApplicationUser.MustChangePassword` so the reset password is replaced at next login.
- Bootstrap fails clearly when a different Admin already exists and the configured bootstrap account is absent. The expected error explains that an Admin already exists but does not match the configured bootstrap account.

### Queue attendance-attempt correction

- The intended rule is one QueueEntry per attendance attempt, not a 1:1 Appointment-to-QueueEntry relationship. Resolved in Step 2C-2: the unique index on `QueueEntries.AppointmentId` is filtered to live attempts (`Waiting`, `Called`, `InConsultation`).

## Assumptions — pending user confirmation

- None.

## Toolchain decision

- The original technical plan referenced .NET 11 / `net11.0`, but SDK 11.0.100 is not installed in the development environment. The repository was retargeted to `net10.0` using the available and pinned SDK 10.0.401; EF Core and ASP.NET Core Identity packages are 10.0.0.
- The installed `dotnet-ef` CLI is 11.0.0 while the project packages are 10.0.0. Recommend using the matching 10.0.0 CLI when the tool is next updated; do not install or change it as part of this decision.

## Grace Period policy deviation

- The confirmed policy is Grace Period = 3 × the doctor's expected consultation duration, using the configured appointment duration fallback when the historical sample is insufficient or the threshold is unset. Freeze the computed minutes and start timestamp on the appointment.
- This is a deliberate deviation from the specification's configurable `ClinicSettings.LateArrivalGracePeriodMinutes` field. The per-doctor calculated policy was selected instead of a clinic-wide fixed setting.

## Doctor leaves early / mass disruption

- Reception/Admin only records a disruption: Doctor, date, from-time, and required reason. They do not authorize a schedule extension or change the doctor's working hours.
- Block the doctor's remaining time using the existing dated unavailable-period mechanism.
- Create a worklist containing affected Confirmed appointments and patients already checked in and waiting. Handle each affected patient individually and manually. Record who contacted the patient, when, notes, and one of these outcomes: Pending, Contacted, Rescheduled, or Cancelled.
- If an appointment remains unresolved when its appointment time passes, cancel it with the reason `clinic unavailable`. It must never become No-Show and must be excluded from queueing analytics.
- Step 2B adds a check-in warning for flagged appointments only. Step 2C implements the full disruption workflow and tests.

## Notifications (Phase 3)

- Day-end recording is confirmed per Doctor and per disruption, not clinic-wide. When Reception explicitly records that a doctor's day ended with unresolved appointments, cancel those appointments with reason `clinic unavailable` and automatically notify each affected patient in addition to the manual contact record.
- Automatically notify a patient when an unresolved appointment is cancelled as its appointment time passes.
- Until a provider is selected, build a notification record and provider interface with in-app notifications only.

## Decisions confirmed 2026-11-24

### 2B review fixes

- Identity does not require an email (`RequireUniqueEmail` is off) so phone-only patients can be created. Non-null emails stay unique: the service checks first and the Identity `EmailIndex` on `NormalizedEmail` is a unique index filtered to `IS NOT NULL`.
- Login accepts a patient's phone in any supported Egyptian format and normalizes it to the canonical `+20` username; other usernames (staff emails) are only trimmed.
- The bootstrap seeder never promotes an existing non-Admin account. If the configured bootstrap email belongs to a non-Admin user, startup fails with a clear error.
- Changing a password refreshes the sign-in cookie so the user stays signed in.
- Patient phone ownership is not verified by OTP: third-party SMS gateways are out of scope in the business idea. Patient account creation by staff stays Receptionist-only.

### Queue-entry link after Grace Period expiry

- Keep `AppointmentId` null on the walk-in QueueEntry created when a patient arrives after their Grace Period expired (appointment becomes No-Show). Reporting correlates that attempt to the missed appointment by patient and clinic date.
- The unique index on `QueueEntries.AppointmentId` is narrowed to live attempts (`Waiting`, `Called`, `InConsultation`), so one appointment can have several attendance attempts over time but only one live one.

### Medical attachments

- Store attachment files on disk in a configured folder outside `wwwroot`. Files are served only through an authorized controller action; the database stores metadata only (original name, content type, size, stored file name, uploader, timestamp). The folder needs its own backup.

### Analytics model

- The dashboard shows measured metrics from real timestamps (λ, μ, ρ, Wq, Ws, Lq, Ls with Little's Law) and an M/G/1 theoretical comparison (Pollaczek–Khinchine using the measured service-time mean and variance). The theoretical side is shown only when ρ < 1; ρ ≥ 1 is flagged as overload. Appointments cancelled with reason `clinic unavailable` are excluded.
- Review follow-up (2026-11-28, `docs/QUEUEING_MODEL_REVIEW.md` phase 1): λ and ρ stay arrivals-based (demand), so the live board shows a growing queue before those patients are served; the Little's Law line uses the served throughput, like its percentage. The Little's Law gap is presented as waiting outside open hours, not as a check of timestamp accuracy. Busy time counts long consultations up to the cap; the cap still keeps them out of the mean and variance.
- Review follow-up (2026-11-28, `docs/QUEUEING_MODEL_REVIEW.md` phase 2): open time `T` includes demonstrated work outside the schedule: each consultation counted up to the cap, and per clinic-local day the spans from the first consultation start to the scheduled start and from the scheduled end to the last consultation end (`ScheduledOpenHours` keeps the schedule-only value). Disruptions need no change, because recording one already creates dated breaks, so `T` already excludes that time. Kingman G/G/1 is shown next to M/G/1 as a second reference, with `Ca²` from same-day arrival gaps, because booked arrivals are more regular than random. The live mean consultation is blended with the doctor's last 30 days of valid consultations, with weight at most 5. Walk-in estimates count not-yet-arrived bookings (NotArrived, GraceRunning) fully (GraceExpired is not counted), and their ranges are 80% (z = 1.28).
- Review follow-up (2026-11-28, `docs/QUEUEING_MODEL_REVIEW.md` phase 3): working-hour history is kept in a new append-only table, `DoctorWorkingHourSnapshots`, instead of changing `DoctorWorkingHours`, so that table keeps its unique index and row identity. A legacy window (one with no snapshot) gets a baseline of its old state, effective from `DateOnly.MinValue`, at its first change. `QueueEntries.ClosedAtUtc` stays null for stale closes (the maintenance job runs on a later day, so the real leave time is unknown) and for entries recorded before deploy. The Little's Law pair stays on served patients, so its gap still means the waiting outside open hours; the waiting-room and in-clinic averages, which count leavers, are a separate view. The priority model (Cobham's non-preemptive priority M/G/1) is a third reference next to M/G/1 and Kingman. The migration is additive and is applied manually to each database before the code is deployed; the app does not migrate at startup.

### Appointment duration changes (2026-11-28)

- A change of a doctor's appointment duration takes an effective date (tomorrow at the earliest, chosen on the schedule page). Today and earlier days keep the old duration; `Doctor.DurationOn(date)` gives the duration for any date and the daily maintenance promotes a due change.
- From the effective date, each day's Pending and Confirmed appointments are re-timed in place onto that day's new grid in the same order (nearest slot, leaving room for the rest). Pending stays Pending. When a day no longer fits, its last window gets an Approved extension aligned to the new grid (an existing one is extended), so everyone keeps their booked day.
- All or nothing: the change is refused, with nothing saved, when an affected day has an open disruption item, a new time would overlap the patient's appointment with another doctor, or an extension would run into a later window or past 23:55.
- Moves are saved one by one in a fixed order (later moves from the end of the day, then earlier moves from the start) so the filtered unique index on live appointment starts is never violated. Each patient whose start changes gets an in-app AppointmentRescheduledByClinic notification, published after the commit. A reminder already sent keeps the old time.
- Cancelling a scheduled change moves the appointments back; extensions it created stay in place.

### Doctor breaks

- Dated breaks only. Recurring weekly breaks are not supported.

### Decisions after the business-logic review (2026-11-24)

- **Walk-in FIFO with approvals:** Call and Call Next take the earliest walk-in in FIFO order (`ArrivedAt`, then `QueueSequence`) that holds a usable approval. A walk-in without an approval, or whose request was rejected, keeps its FIFO position and does not block approved walk-ins behind it; it is called in FIFO order once approved.
- **Approved admission permit lifetime:** an Approved permit is valid until the end of the clinic day on which it was approved (single use). The configurable pending duration applies only to Pending requests.
- **Checked-in patients during a disruption:** a patient already checked in and waiting is never cancelled automatically. Staff handle them from the worklist; whatever is still unresolved when Reception records day end is cancelled as `clinic unavailable`. The time-based automatic cancellation applies only to appointments whose patient has not checked in.
- **After a doctor leaves early:** from the disruption's from-time, that doctor cannot be called for (scheduled Call, walk-in Call, Call Next) and new walk-ins cannot be registered for them that day.
- **Deactivating a doctor:** deactivation cancels all of the doctor's future Pending/Confirmed appointments with a recorded reason (doctor no longer available) and notifies each patient, in one operation.
- **Booking limits:** a patient cannot hold two live (Pending/Confirmed) appointments that overlap in time, with any doctors. No booking horizon or open-request limit.
- **Clinic bookings:** an appointment booked by Reception/Admin for a patient is created Confirmed in one transaction (Reception is the confirming authority). This also serves a walk-in who accepts an alternative appointment.
- **Patient cancellation with a pending reschedule:** cancelling the original also cancels its live Pending replacement. A patient replacement cannot be confirmed once the original appointment's clinic-local day has arrived; Reception rejects it instead.
- **Stale queue entries:** Waiting/Called entries left from a previous clinic day are closed as Cancelled by the maintenance job. A consultation left InConsultation from a previous day is completed automatically by the same job (and its appointment marked Completed) so it cannot block the doctor's one-active-consultation rule; such durations exceed the 60-minute cap and are therefore excluded from expected-duration and analytics.

### Account recovery and sign-in hardening (approved 2026-11-24)

- **Staff password reset:** Admin can reset the password of a Doctor or Receptionist account (not an Admin account and not their own). The system generates a temporary password shown once to the Admin, sets `MustChangePassword`, ends the account's existing sessions (security stamp), and clears any lockout.
- **Patient password reset:** Receptionist or Admin can reset a Patient account's password the same way, after verifying the patient's identity in person (no SMS/email in scope). The page reminds staff to verify identity.
- **Password policy:** Identity requires at least 12 characters (matching the UI), with the default character-class rules. Lockout after 5 failed attempts for 15 minutes, also for new users.
- **Rate limiting:** built-in ASP.NET Core rate limiting on sign-in, registration and password-change POSTs, per client IP, with limits in configuration; exceeding them returns HTTP 429 with a friendly page.
- **Security headers:** Content-Security-Policy (scripts only from the site itself; no inline script), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, and a restrictive `Permissions-Policy`. Inline script handlers in views move to `site.js`. Auth cookie is HttpOnly, SameSite=Lax, and Secure outside Development.
- **Page-level integration tests:** add `Microsoft.AspNetCore.Mvc.Testing` (test project only) to run the real application against a disposable LocalDB database and request real pages.

## Open / decision required

- Minimum consultation sample-size value. The setting exists (Admin → Settings, `ClinicSettings.MinimumConsultationSampleSize`); it has no hardcoded default and is unset until the clinic chooses a value.
- Notification delivery provider beyond in-app notifications (in-app only until chosen).
- Admin access to medical content. The specification says "policy-gated" without a policy; Admin currently has no access to medical records, prescriptions or attachments.
- Removing `MediCare.Infrastructure/Notifications/NoOpPatientNotificationPublisher.cs`, which is no longer used (deletion needs approval).
- Analytics: whether Receptionists should also see historical analytics (currently live board only), the historical cache lifetime (5 minutes), and a maximum report range (none).

## Analytics definitions (Phase 4)

- The no-show rate counts only appointments with a final attendance outcome: NoShow / (Completed + NoShow), in the period and not after today, excluding `clinic unavailable` cancellations. Full model and assumptions: `docs/ANALYTICS_MODEL.md`.

## Current implementation boundary (2026-11-24)

- Phases 1–4 are implemented. See `docs/PROGRESS.md` for the checklist and `docs/WORK_LOG.md` for step history.
- All persisted timestamps are UTC `DateTimeOffset`; clinic-local dates and times go through `IClinicClock` with the DST rules above.
- Dated doctor breaks only (no recurring breaks).
- In-app notifications only; no SMS/email provider and no real-time channel (refreshable pages).
- All migrations through `20261124112210_ClientGeneratedKeys` are applied to the configured local development database (LocalDB `MediCare`, which held no data) with the user's approval on 2026-11-24. SQL review scripts remain in `artifacts/`. SQL integration tests still use a disposable LocalDB database.
- Domain entities generate their own Guid keys; EF is configured with `ValueGeneratedNever` for every single-Guid domain key so children added through navigations are inserted. Prescription items are replaced as a whole (`ClientCascade`).
- All user-facing times are shown in clinic-local time (`ClinicTime.Format`); UTC is never displayed.
- All role flows were exercised in a browser on 2026-11-24, including analytics, Doctor statistics, extensions and the disruption workflow (see WORK_LOG).
- Known limitation: analytics open time for past dates uses the schedule in force on each day only from deployment of the working-hour snapshots (phase 3). Earlier history is not reconstructed, and a legacy window deactivated before then has an unknown past.
