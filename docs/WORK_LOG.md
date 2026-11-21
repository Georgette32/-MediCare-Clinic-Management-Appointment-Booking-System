# MediCare Work Log

## Phase 1 — Foundation & Core Architecture

- Created the .NET 11 solution and Clean Architecture project skeleton.
- Added the intended project references and pinned the repository SDK to 11.0.100.
- Kept EF Core, Identity, domain persistence, and migrations out of this phase because their implementation depends on unresolved project decisions.
- Verification: `dotnet build MediCare.slnx` with SDK 11.0.100 succeeded with 0 warnings and 0 errors.
- Database changes: none.

## Phase 2 — Appointment & Queue Management

- Inspected the current solution: it contains only the ASP.NET Core MVC starter UI and empty Domain, Application, and Infrastructure projects. There are no entities, persistence, Identity configuration, or appointment/queue features yet.
- Recorded the user's appointment and queue decisions in `docs/DECISIONS.md` and updated `docs/PROJECT_CONTEXT.md`.
- Phase 2 plan approved by the user.
- Implemented appointment requests that reserve a configured doctor schedule slot as Pending, and receptionist confirmation/rejection with an independent `Receptionist` role.
- Added SQL Server EF Core and ASP.NET Core Identity infrastructure, including separate role names for Admin, Doctor, Receptionist, and Patient.
- Added a Reception-only pending request list and confirm/reject actions. Appointment length is read from the doctor's working-hour configuration; no default is supplied.
- Booking validates active doctor and patient records, configured weekday work hours, dated breaks, holidays, slot alignment, and pending/confirmed overlap. Booking and confirmation updates use serializable transactions.
- Build verification: `dotnet build MediCare.slnx` succeeded with 0 warnings and 0 errors.
- Tests were not added or run.
- No database was created or modified. No migration was generated or applied.
- Phase 2 remains in progress. Patient request intake, queue admission/approval, Grace Period, recurring breaks, schedule maintenance UI, staff account provisioning, cancellation, and rescheduling remain open or incomplete; see `docs/DECISIONS.md`.

### Step 2A — schema alignment, account links, queue entity, appointment history

- Updated after the user's 2A approval and amendments.
- Moved configured duration from working-hour rows to Doctor.
- Added required account links to Patient and Doctor.
- Added QueueEntry source/status enums, factories, transition validation, nullable AppointmentId, and walk-in-only QueueSequence.
- Added patient cancellation before appointment day and linked appointment rescheduling transitions.
- Tests: `dotnet test MediCare.Tests\MediCare.Tests.csproj` — passed, 16 passed, 0 failed.
- Build: `dotnet build MediCare.slnx` — succeeded, 0 warnings, 0 errors.
- No tests cover 2B/2C behaviors yet; those are assigned to the later gated steps.
- No migration generated or applied; no database changes.
- Stop here for 2A review. Generate the initial migration SQL only after explicit 2A approval.

### Step 2A fix round — rescheduling atomicity and clinic-local dates

- Kept the original appointment Confirmed while a patient replacement is Pending. Confirmation now updates replacement and original in one serializable transaction; cancellation/rejection preserves the original.
- Added clinic reschedule behavior: Reception/Admin replacement is Confirmed and original becomes Rescheduled in one transaction; direct cancellation requires a reason.
- Excluded the linked original from overlap checks and limited replacement uniqueness to Pending/Confirmed. The one-active-consultation index remains filtered to InConsultation only.
- Added an injectable clinic clock, required configurable `Clinic:TimeZoneId` (deployment value `Africa/Cairo`), local-date comparison, UTC timestamp normalization, and Windows/IANA zone resolution.
- Recorded the confirmed expected-duration rules, replacement behavior, day boundary, and clinic cancellation behavior in `docs/DECISIONS.md` and `docs/PROJECT_CONTEXT.md`.
- Tests: `& "$env:USERPROFILE\.dotnet\dotnet.exe" test MediCare.Tests/MediCare.Tests.csproj --no-restore` — passed, 32 passed, 0 failed. Includes booking/cancellation/rescheduling application-service tests, day boundary, Cairo DST transition, and model-index filters.
- Build: `& "$env:USERPROFILE\.dotnet\dotnet.exe" build MediCare.slnx --no-restore` — succeeded, 0 warnings, 0 errors.
- SQL Server constraint-violation tests were not run; they are explicitly deferred until after migration approval. No migration was generated or applied; no database was touched.
- Waiting for user review before migration generation or 2B.

### Documentation update — mass disruption and notifications

- Confirmed Reception/Admin replacement behavior; removed the prior assumption status.
- Recorded doctor-leaves-early handling: Reception/Admin record doctor/date/from-time/reason, block the remaining time through the existing dated unavailable mechanism, and handle affected confirmed appointments and checked-in waiting patients individually with manual-contact details and outcomes.
- Recorded that unresolved appointments are cancelled at appointment time with `clinic unavailable`, never marked No-Show, and excluded from queueing analytics.
- Set 2B scope to a check-in warning for flagged appointments only; full disruption workflow and tests are assigned to 2C.
- Recorded Phase 3 notifications for explicit day-end closure and appointment-time cancellation, with an in-app notification record/provider interface until provider choice.
- Open decisions now list day-end handling scope (per-doctor disruption assumed), notification provider, minimum sample-size value, recurring breaks, analytics model, and file storage provider.
- Documentation only; no code, migration, or database changes. Step 2 stops here for review before migration SQL generation.

### Initial migration generation — SQL review only

- Per-doctor day-end recording is confirmed and removed from the Open list in `docs/DECISIONS.md` and `docs/PROJECT_CONTEXT.md`.
- Generated the initial EF Core migration and model snapshot under `MediCare.Infrastructure/Persistence/Migrations/`.
- Generated `artifacts/InitialCreate.sql` for review. The script includes Identity and domain tables, indexes, check constraints, and foreign keys.
- The migration source and SQL script have not been applied. No database was created, modified, or connected to for this operation.
- No 2B work started.

### Initial migration revision — SQL review only

- Added database-level uniqueness for Pending/Confirmed doctor-date-start appointments, retaining the serializable overlap check for partial overlaps. Same-doctor/date/start rescheduling is explicitly rejected; changing doctor, date, or start time remains allowed.
- Added appointment, break, working-hour, and holiday date/time-range check constraints.
- Replaced the free-text doctor specialization with a required restricted FK to a `Specializations` table after user confirmation. Name is unique; Description is optional.
- Limited Identity `FullName` to 200 characters to match Doctor and Patient profiles.
- Tests: `dotnet test MediCare.Tests/MediCare.Tests.csproj --no-restore` — passed, 39 passed, 0 failed.
- Regenerated InitialCreate and its SQL review script after deleting the previous unapplied InitialCreate. SQL Server documentation explicitly demonstrates `IN (...)` in filtered-index predicates.
- The regenerated script was not applied; no database was touched. 2B remains unstarted.
