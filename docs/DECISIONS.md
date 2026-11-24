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

- The intended rule is one QueueEntry per attendance attempt, not a 1:1 Appointment-to-QueueEntry relationship. The current EF model still has a unique filtered index on `QueueEntries.AppointmentId`, which prevents multiple entries for one appointment. Removing that constraint and reconciling check-in behavior is an implementation gap outside the Step 2B account-provisioning scope; it needs a separately reviewed migration and tests.

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

## Open / decision required

- Minimum consultation sample-size value (setting must remain configurable and must not have a hardcoded default).
- Notification delivery provider beyond in-app notifications (in-app only until chosen).

## Current implementation boundary

- Step 2A implements account links, per-doctor configured duration, appointment history links, QueueEntry state transitions, booking validation, patient and clinic cancellation/rescheduling, and clinic-local date checks through an injectable clock.
- Step 2B.1 implements `IQueueRepository`, `EfQueueRepository`, `ICheckInService`, and `CheckInService` for scheduled check-in and daily walk-in FIFO sequence generation under serializable transactions. Step 2B Step 3 adds doctor-bound scheduled check-in, grace-expiry handling, walk-in eligibility checks, separate queue ordering queries, bounded SQL concurrency retry, Reception/Admin endpoints, and the warning-provider interface.
- The current repository also implements expected-duration calculation from completed visits and Grace Period start/freeze and expired-period No-Show processing. Grace Period starts no earlier than the scheduled start time and expiry processing is restricted to the current clinic date.
- Phase 2C Step 1 implements per-entry walk-in admission approval records and the separate one-time Call operation. Working-hour extensions and doctor-leaves-early disruption handling remain outside this step. `ICheckInWarningProvider` is an interface-only extension point; disruption warning implementation is deferred.
- Automatic notification behavior and the in-app notification record/provider interface belong to Phase 3.
- Doctor breaks currently support dated entries only; recurring breaks remain unimplemented pending a decision.
- Persisted timestamps use DateTimeOffset and are normalized to UTC by appointment/queue operations.
- QueueSequence is walk-in-only; check-in workflows supply clinic-local QueueDate from `IClinicClock.Today`.
- The initial and `InitialCreate_net10` migrations have been generated; read-only EF history inspection found both applied to the configured local MediCare database. The stabilization task did not update that database. The full SQL Server integration-test suite creates a separate disposable LocalDB, applies migrations for its checks, and drops it during fixture cleanup.
