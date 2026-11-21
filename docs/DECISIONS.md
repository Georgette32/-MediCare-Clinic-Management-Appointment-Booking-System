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

- There is one QueueEntry per attendance attempt. `AppointmentId` is nullable: scheduled attendance links to its appointment; a walk-in has no appointment link.
- A No-Show patient's later arrival creates a new walk-in QueueEntry with no appointment link. Preserve the No-Show appointment and its history.
- Scheduled patients are ordered by appointment time. Walk-ins are ordered FIFO by actual `ArrivedAt`; `QueueSequence` is walk-in-only and is a deterministic tie-breaker. Scheduled entries have no sequence. Never combine these orderings.
- Grace Period begins at the scheduled appointment time when the patient has not checked in. The patient retains scheduled priority. If an approved replacement consultation is already in progress when they arrive within the Grace Period, they wait until that consultation finishes.
- Expected consultation duration is the per-doctor average over the trailing 30 clinic-local days. Count only Completed consultations with valid timestamps and durations satisfying `0 < duration < 60 minutes`.
- The minimum consultation sample size is configurable and has no hardcoded value or default. If it is unset or the sample is below the threshold, use that doctor's configured appointment duration for both timeline estimates and Grace Period calculation.
- The 60-minute consultation-duration cap must be represented as a named constant or setting, not a scattered literal.
- Grace Period is per doctor and is three times expected consultation duration. Freeze the calculated Grace Period on the appointment when its timer starts; do not recalculate it live.
- Every walk-in admission needs a separate Doctor approval or rejection. Record each decision; an approval cannot be reused for another admission. Only Reception creates the request, and only the Doctor linked to that doctor profile decides. Reception cannot approve admission.
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

## Assumptions — pending user confirmation

- None.

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

## Open / decision required

- Notification provider choice.
- Minimum consultation sample-size value (setting must remain configurable and must not have a hardcoded default).
- Recurring doctor break schedule design.
- Analytics model.
- Medical-file storage provider.

## Current implementation boundary

- Step 2A implements account links, per-doctor configured duration, appointment history links, QueueEntry state transitions, booking validation, patient and clinic cancellation/rescheduling, and clinic-local date checks through an injectable clock.
- QueueEntry ordering/check-in workflows, walk-in approvals, Grace Period, no-show processing, working-hour extensions, historical expected-duration calculation, timeline fallback, and doctor-leaves-early handling remain for Steps 2B/2C or later phases. Step 2B is limited to a check-in warning for flagged appointments; the full disruption workflow and tests are in 2C.
- Automatic notification behavior and the in-app notification record/provider interface belong to Phase 3.
- Doctor breaks currently support dated entries only; recurring breaks remain unimplemented pending a decision.
- Persisted timestamps use DateTimeOffset and are normalized to UTC by appointment/queue operations.
- QueueSequence is walk-in-only; the check-in workflow planned for 2B must supply a clinic-local QueueDate.
- The initial EF Core migration and review SQL script have been generated. Neither has been applied; no database has been created or modified.
