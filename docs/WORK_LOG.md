# MediCare Work Log

## Phase 1 — Foundation & Core Architecture

- Created the Clean Architecture solution. Although the original technical plan referenced .NET 11 / `net11.0`, SDK 11.0.100 was not installed in the development environment; the repository was retargeted to `net10.0` and pins the available SDK 10.0.401.
- EF Core and ASP.NET Core Identity project packages are 10.0.0.
- Kept EF Core, Identity, domain persistence, and migrations out of this phase because their implementation depends on unresolved project decisions.
- The initial build verification entry was superseded by the Phase 2B stabilization results below.
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
- The regenerated script was not applied; no database was touched.

### Step 2B.1 — Queue Repository and Check-In Service (Arrival & FIFO Sequence)

- Implemented `IQueueRepository` and `EfQueueRepository` providing query operations, active queue lookups, duplicate-check-in verification, and daily doctor-specific walk-in sequence resolution (`GetNextWalkInSequenceAsync`).
- Implemented `ICheckInService` and `CheckInService` with serializable transaction boundaries coordinating:
  - Scheduled patient check-in: verifies appointment exists, is Confirmed, matches `IClinicClock.Today`, prevents duplicate check-in, and creates `QueueEntry.CreateScheduled` with null sequence and UTC arrival timestamp.
  - Walk-in patient arrival registration: verifies active patient and doctor, prevents duplicate active queue entry for the doctor today, queries next positive daily sequence for (`DoctorId`, `Today`), and creates `QueueEntry.CreateWalkIn`.
- Registered `IQueueRepository` and `ICheckInService` in `MediCare.Infrastructure/DependencyInjection.cs`.
- Historical Step 2B.1 test run was recorded as 61 passed, 0 failed (13 new check-in tests). The current full-suite result is recorded in the stabilization entry below.
- Build: `dotnet build MediCare.slnx` — succeeded, 0 warnings, 0 errors.
- Database changes: none. No migration was generated or applied.

### Phase 2B stabilization

- Corrected GracePeriodServiceTests to use the existing `GracePeriodStartResult` properties. Added guards and tests so Grace Period cannot start before the appointment's local scheduled start time and expiry processing accepts only `IClinicClock.Today`.
- Corrected the documented toolchain: SDK 10.0.401, `net10.0`, EF Core/Identity 10.0.0. The installed `dotnet-ef` CLI is 11.0.0; recommend aligning it to 10.0.0 when next updated. No tool was installed.
- `dotnet build MediCare.slnx`: succeeded, 0 errors, 12 NU1902/NU1903 vulnerability warning instances for `System.Security.Cryptography.Pkcs` 9.0.4.
- `dotnet test MediCare.slnx`: 83 total, 79 passed, 4 failed, 0 skipped. Remaining failures: three ExpectedDurationServiceTests expect an unset minimum sample size to use the historical average despite the decision requiring fallback to configured appointment duration; the SQL Server integration test expects one migration while the current model has two (`InitialCreate` and `InitialCreate_net10`).
- `artifacts/InitialCreate.sql` remains stale with ProductVersion 11.0.0. Leave it unchanged; regenerate the review SQL when a migration is next explicitly approved.
- No migration or schema change was applied to the application's configured database, and no `dotnet ef database update` was run. The full test command did execute the SQL Server integration-test fixture, which created an isolated disposable LocalDB, applied migrations for its integration checks, and dropped that database during fixture cleanup.

### Phase 2C Step 1 — Walk-in Admission Approval

- Added per-QueueEntry walk-in approval records with Pending, Approved, Rejected, and Expired decisions. The record is tied to one walk-in and its owning Doctor; only one Pending request per entry is allowed. Rejection leaves the entry Waiting in its FIFO position, and a later request is allowed.
- Approval is a record/permit only. Reception/Admin submit and Call; only the owning Doctor can decide. The Call operation revalidates and consumes an unused same-day approval inside a serializable per-doctor/date lock before changing Waiting to Called. Generic `QueueEntry.Call` now rejects walk-ins.
- Call checks doctor queue availability, checked-in scheduled priority, FIFO order, and normal working hours. It does not alter the replaced appointment or its frozen Grace Period values. Pending expiry uses configured duration (default 30 minutes) capped at clinic-day end; approved records cannot be used on a later clinic date.
- Added server-authorized Reception/Admin request and Call POST actions and a Doctor refreshable request list/decision action, with anti-forgery validation on POSTs.
- Generated migration `20261123020059_AddWalkInAdmissionApprovals` and review SQL at `artifacts/AddWalkInAdmissionApprovals.sql`. SQL creates the approval table, foreign keys, decision/timestamp check, pending-only unique index, doctor lookup index, and migration-history row. Neither the migration nor script has been applied; no `dotnet ef database update` was run.
- `dotnet build MediCare.slnx`: succeeded, 0 errors, 12 NU1902/NU1903 warning instances for System.Security.Cryptography.Pkcs 9.0.4.
- `dotnet test MediCare.slnx`: 123 total, 123 passed, 0 failed, 0 skipped. The SQL integration fixture uses a disposable LocalDB, applies migrations there, and drops it at cleanup; the configured application database was not updated.
- Known limitation: no working-hour extension entity exists, so Call checks normal configured hours only. No SignalR, disruption handling, or opportunity entity was added. Awaiting review of the generated SQL before any application.

### End-of-day progress update — 2026-11-23

- Re-read `Technical Specification.txt` §19 (approval/change-control instructions), §17 (phase checklist), `docs/DECISIONS.md`, and this work log. Created `docs/PROGRESS.md` with a current-code Section 17 checklist: Phase 1 12/12, Phase 2 15/16, Phase 3 0/8, Phase 4 1/11, overall 28/47.
- **Phase 2B stabilization / tests:** fixed Grace Period result-property references and added/fixed grace guards and Expected Duration tests. Touched `MediCare.Application/Services/GracePeriodService.cs`, relevant application interfaces, `MediCare.Tests/GracePeriodServiceTests.cs`, `ExpectedDurationServiceTests.cs`, and `SqlServerConstraintIntegrationTests.cs`. The existing log records the pre-fix full run as 83 total, 79 passed, 4 failed; the isolated full-suite count immediately after those fixes was not retained. Those changes are included in later passing full-suite runs.
- **Grace Period anchor and DST:** changed the anchor to the appointment's scheduled local start converted by `IClinicClock`; kept inclusive expiry; added standard-time resolution for ambiguous Cairo local times and first-valid-after-gap resolution. Touched `MediCare.Domain/Entities/Appointment.cs`, `MediCare.Application/Abstractions/IClinicClock.cs`, `MediCare.Application/Services/GracePeriodService.cs`, `MediCare.Infrastructure/Time/SystemClinicClock.cs`, related appointment/check-in/grace/clock tests, and `docs/DECISIONS.md`. Intermediate suite count was not retained separately; it is covered by the cumulative passing suite below.
- **Phase 2B Check-In Service:** implemented scheduled arrival validation and grace expiry, walk-in registration with deterministic doctor/date FIFO sequence and bounded infrastructure retries, ordering repository methods, and Reception/Admin endpoints with server authorization and antiforgery. Touched the Check-In service/repository/unit-of-work abstractions and implementations, `MediCare.Infrastructure/DependencyInjection.cs`, `MediCare.Web/Controllers/ReceptionController.cs`, check-in view models, and check-in/authorization/SQL tests. The subsequent Phase 2C full-suite milestone reported 123 total, 123 passed, 0 failed, 0 skipped.
- **Phase 2C Step 1:** added per-entry WalkIn approval request/decision records, Doctor ownership enforcement, expiry, one-time Reception/Admin Call, Doctor list, and supporting tests. Touched the approval domain entity/enum, application service/options/repository interfaces, EF repository/context/unit of work/DI, Reception and Doctor controllers/view models/Doctor view, development configuration, tests, and migration/snapshot artifacts. The full suite at that milestone was 123 total, 123 passed, 0 failed, 0 skipped.
- **Approval constraint hardening:** after review, tightened the EF check for the four decision strings and allowed timestamp combinations, added six invalid-insert cases, five valid-insert cases, and the pending-only unique-index case. Removed the unapplied migration without `--force` after read-only history verification, then regenerated it with the same name as ID `20261123020059_AddWalkInAdmissionApprovals`. Updated `MediCare.Infrastructure/Persistence/MediCareDbContext.cs`, migration and Designer, model snapshot, `MediCare.Tests/SqlServerConstraintIntegrationTests.cs`, `artifacts/AddWalkInAdmissionApprovals.sql`, and this decisions log. ProductVersion remains 10.0.0; no pending model changes. Full suite: 126 total, 126 passed, 0 failed, 0 skipped.
- **Current verification:** `dotnet build MediCare.slnx` succeeded with 0 errors (12 NU1902/NU1903 warning instances for `System.Security.Cryptography.Pkcs` 9.0.4). `dotnet test MediCare.slnx` completed with 126 total, 126 passed, 0 failed, 0 skipped. SDK is 10.0.401; project targets are `net10.0`; EF Core and Identity packages are 10.0.0; installed `dotnet-ef` CLI is 11.0.0.
- **Read-only migration status:** `dotnet ef migrations list` reports `20261121190210_InitialCreate` and `20261123000418_InitialCreate_net10` applied, with `20261123020059_AddWalkInAdmissionApprovals` pending. No database update was run. SQL tests apply migrations only to the disposable LocalDB fixture and drop that database at cleanup.
- Updated `docs/DECISIONS.md` to record that Call Next is not yet implemented and will serve scheduled entries in appointment-time order before approved walk-ins in FIFO order; walk-in Call currently checks normal working hours only, until Working-Hour Extensions exist.
- This entry covers stabilization, scheduled-start anchor/DST rules, Check-In Service, initial Phase 2C approval, and constraint-hardening milestones. Separate historical full-suite counts for the intermediate 2B fixes and anchor work were not preserved, so no counts are inferred for those runs.
