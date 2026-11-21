# MediCare Project Context

## Current foundation

- The repository began with the business and technical specification documents and now has a .NET 11 solution with Domain, Application, Infrastructure, MVC Web, and test projects.
- The Web starter includes ASP.NET Core Identity integration, SQL Server EF Core configuration, a login screen, and a Reception-only pending appointment confirmation page.
- Appointment requests are validated against doctor weekday working hours, dated breaks, holidays, and pending/confirmed overlap using serializable transactions.
- An active-slot filtered unique index protects doctor/date/start-time against duplicate Pending or Confirmed appointments; the transactional overlap check remains the partial-overlap guard.
- Patient and Doctor profiles now have required one-to-one links to Identity accounts. Guest patient access is not supported.
- Configured appointment duration is stored per Doctor. `DoctorWorkingHour` stores schedule times only.
- QueueEntry has source-specific factories and validated transitions. `QueueSequence` is walk-in-only: scheduled entries have null sequence; walk-ins require a positive sequence.
- Appointment booking, patient cancellation, and rescheduling are coordinated by application services using serializable transactions and an injectable clinic clock.
- Patient cancellation and reschedule requests are rejected on the clinic-local appointment day and allowed before it. Reception/Admin can cancel on the same day.
- A patient replacement request is Pending and linked while the original remains Confirmed. Confirming it marks the original Rescheduled in the same transaction. Rejection/cancellation leaves the original unchanged; failed attempts do not block another attempt.
- A replacement with the same Doctor/date/start time as its original is rejected. Replacement with a different Doctor, date, or start time is allowed if available; the original remains Confirmed until approval.
- Doctor specialization is represented by a required restricted FK to `Specializations` (`Id`, unique `Name`, optional `Description`). `AspNetUsers.FullName` has max length 200, matching Doctor/Patient profiles.
- Check constraints enforce appointment/break/working-hour end time after start time and holiday end date on/after start date.
- Reception/Admin may replace an affected appointment with a directly Confirmed linked replacement in one transaction; the original becomes Rescheduled atomically. If no slot exists, cancel the original with a reason. This is confirmed.
- Slot overlap validation excludes the linked original for rescheduling. The model's live-replacement unique index covers Pending and Confirmed only; the active-consultation index covers InConsultation only.
- Clinic day comparisons use `IClinicClock.Today` and configured timezone conversion rather than constructing local midnight. Timestamps are DateTimeOffset values normalized to UTC. The deployment config sets the clinic timezone to `Africa/Cairo`; no code default is assumed.
- Receptionist remains a separate Identity role from Admin. Admin account provisioning screens are not implemented yet.
- The initial EF Core migration and review SQL script are regenerated for the latest model. Neither has been applied; no database has been created or modified.

## Confirmed decisions

- Patient booking requests remain Pending until Reception confirms or rejects them.
- There is one QueueEntry per attendance attempt. `AppointmentId` is nullable; walk-ins have a null link. A No-Show patient's later arrival creates a new walk-in entry and preserves the original appointment history.
- Scheduled queue ordering uses appointment time. Walk-in FIFO uses actual arrival time, with a walk-in-only sequence tie-breaker.
- Expected consultation duration is the per-doctor average over the trailing 30 clinic-local days, using only Completed consultations with valid timestamps and `0 < duration < 60 minutes`.
- The minimum sample size has no hardcoded value/default and must be configurable. If it is unset or unmet, configured appointment duration is used for the booking timeline and Grace Period.
- Slot generation always uses configured appointment duration. Expected duration drives estimates only.
- Grace Period starts at appointment time if check-in has not occurred, belongs to the doctor, is three times expected consultation duration, and is frozen on the appointment when its timer starts.
- The 60-minute duration cap must be a named constant or setting.
- Each walk-in admission request is created only by Reception; only the Doctor linked to that profile may decide. Record each individual approval/rejection and do not reuse approvals.
- Extensions require explicit, recorded Doctor approval before availability is extended.
- Patients cannot cancel on appointment day; Reception/Admin may cancel that day. Patient reschedule requests follow the same before-day cutoff.
- Patient rescheduling keeps the original Confirmed while its linked replacement is Pending; confirmation atomically marks the original Rescheduled. Failed attempts leave the original unchanged and do not prevent a new attempt.
- All timestamps are stored in UTC. Clinic timezone has no assumed code default; deployment sets `Africa/Cairo`. Use real timezone database/DST behavior and handle Windows/IANA IDs.
- Clinic-local appointment-day comparisons and queue dates must use the configured zone. Tests cover the calendar-day boundary and Cairo DST-change day.
- Analytics `μ` means service rate, not the Grace Period multiplier.

## Assumptions — pending user confirmation

- None.

## Open / decision required

- Minimum consultation sample-size value; it must remain configurable with no hardcoded default.
- Recurring doctor break schedule design.
- Notification provider choice.
- Analytics model.
- Medical-file storage provider.

## Doctor leaves early / mass disruption

- Reception/Admin only records the Doctor, date, from-time, and required reason. Recording a disruption blocks the doctor's remaining time using the existing dated unavailable-period mechanism.
- The worklist includes affected Confirmed appointments and already checked-in waiting patients. Staff handle entries individually and manually, recording contacted-by, contacted-at, notes, and outcome (`Pending`, `Contacted`, `Rescheduled`, or `Cancelled`).
- An unresolved appointment is cancelled at its appointment time with reason `clinic unavailable`; it is never No-Show and is excluded from queueing analytics.
- Step 2B adds a check-in warning only for flagged appointments. Step 2C implements and tests the full workflow.

## Notification scope — Phase 3

- Day-end recording is confirmed per Doctor and per disruption, not clinic-wide. When Reception explicitly records that a doctor's day ended with unresolved appointments, cancel those appointments with reason `clinic unavailable` and notify each affected patient automatically, in addition to manual contact. Cancelling an unresolved appointment at its appointment time also triggers a notification.
- Until a provider is selected, use a notification record plus provider interface and in-app delivery only.
- Provider choice remains open.

## Architecture references

- Detailed business rules: `Business Idea.txt`.
- Technical architecture, workflows, testing strategy, and approval requirements: `Technical Specification.txt`.
- Decision status and implementation boundary: `docs/DECISIONS.md`.
- Phase history: `docs/WORK_LOG.md`.
