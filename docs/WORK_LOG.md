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

### Phase 2B — Account Provisioning and First-Login Security

- Updated `docs/DECISIONS.md` with the approved Admin provisioning rules, Reception patient-account creation, Egyptian mobile normalization, random one-time temporary passwords, the required first-login password change, and secure initial Admin bootstrap requirements.
- Corrected the queue decision to one `QueueEntry` per attendance attempt. Recorded that the current unique filtered index on `AppointmentId` conflicts with that decision; changing queue persistence is outside this account-provisioning step and remains a separately reviewed schema change.
- Added Admin-only Doctor and Receptionist account creation. Doctor Identity user and Doctor profile creation share a serializable transaction; appointment duration is required and must be positive, with no default. Receptionist and patient account creation also uses transactions.
- Added Reception-only patient account creation. Egyptian mobile phone numbers are normalized to canonical `+20` usernames; the patient phone number and Identity username are unique. Supported forms and invalid-number behavior are documented and covered by unit tests.
- Added cryptographically generated temporary passwords, displayed only in the immediate no-store response to the creating staff member. Added persisted `ApplicationUser.MustChangePassword` and middleware between authentication and authorization that redirects users until they change their password, while allowing static assets and password-change/logout endpoints.
- Added first-Admin bootstrapping from required `BootstrapAdmin:Email`, `BootstrapAdmin:Password`, and `BootstrapAdmin:FullName` secure configuration, with .NET User Secrets enabled for local development. No credential defaults were added; application startup does not apply migrations.
- Generated migration `20261123224137_AddAccountProvisioningSecurity` and review SQL at `artifacts/AddAccountProvisioningSecurity.sql`. It adds `MustChangePassword` (existing users default false) and a unique Patient phone index. `InitialCreate` was not edited. The migration and SQL script have not been applied; no database or existing data was changed. Existing duplicate Patient phone values could prevent the unique index from being applied and must be checked before approval/application.
- Added tests for supported and invalid phone formats, required doctor duration, duplicate accounts, temporary-password generation, patient canonical username, transaction rollback orchestration, secure-bootstrap refusal without configuration, password-change gate behavior, role authorization, and persistence-model changes.
- Build: `dotnet build MediCare.slnx --no-restore` succeeded with 0 errors; six NU1902/NU1903 warning messages for the existing `System.Security.Cryptography.Pkcs` 9.0.4 dependency remain.
- Tests: `dotnet test MediCare.slnx --no-restore --filter "FullyQualifiedName!~SqlServerConstraintIntegrationTests"` passed 140 tests, 0 failed. The SQL Server integration class was excluded because its fixture automatically applies all pending migrations, including this unreviewed migration. The full suite therefore remains pending review/approval of the SQL script and permission to run that fixture.

### 2B review round 2 — Patient self-registration and security verification

- Implemented public Patient self-registration at `/Account/Register`: required Egyptian mobile normalized to canonical `+20` username/profile phone, optional email, patient-chosen password, Patient role, and Identity user plus Patient profile creation in a single transaction. Self-registered patients do not receive a temporary password and do not have `MustChangePassword` set. Added a Login-page registration link.
- Verified `Patient.ApplicationUserId` is a non-nullable `Guid` with a required one-to-one FK. The global unique patient-phone index therefore matches the current model.
- Added tests covering valid 011/012/015 formats, rejected bare `20`, registration with/without email, canonical phone/username, role/password/change-flag handling, phone and Identity username conflicts, and SQL Server duplicate phone rejection.
- Added a disposable SQL Server integration test that inserts a pre-migration Identity row, applies the account-security migration, and verifies `MustChangePassword` is false. Added a real transaction rollback test: Identity user insertion occurs through `UserManager` inside the SQL transaction, a later Doctor-profile repository failure is injected, and the database is verified to contain no user afterward.
- Recorded in `DECISIONS.md` that bare `20` input remains rejected, future Admin password resets must set `MustChangePassword`, and bootstrap reports clearly when another Admin already exists. The seeder's existing error states that an Admin exists but does not match the configured bootstrap account.
- `dotnet list MediCare.Web/MediCare.Web.csproj package --vulnerable --include-transitive` confirmed Pkcs 9.0.4 as high and moderate severity. After the approved tests passed, pinned transitive `System.Security.Cryptography.Pkcs` to 9.0.21 in the Infrastructure project. Re-running the vulnerability command reported no vulnerable packages.
- Full suite with the SQL Server LocalDB fixture: 168 passed, 0 failed, 0 skipped (recorded baseline: 126; increase: 42). The fixture created a unique disposable LocalDB database, applied migrations there, and dropped it during cleanup. No non-disposable database was migrated.

### 2B review round 3 — review fixes (2026-11-24)

- Review of round 2 found that `RequireUniqueEmail = true` made Identity reject every user without an email (verified against Identity 10.0.0), so Reception-created patients and email-less self-registration failed in production; unit tests used fakes and missed it. Removed the option; non-null emails stay unique through the service check and the Identity `EmailIndex`, now a unique index filtered to `[NormalizedEmail] IS NOT NULL`. `IdentityAccountStore` maps unique-index races to a friendly failure. Identity registration is extracted to `AddMediCareIdentity` so tests use the production configuration.
- Login normalizes Egyptian phone input to the canonical `+20` username (`LoginUserName.Resolve`); staff emails are only trimmed.
- The bootstrap seeder refuses to promote an existing non-Admin account that uses the configured bootstrap email.
- Password change refreshes the sign-in cookie after the security stamp changes; the in-memory flag is restored on failure.
- Login shows the registration status message; the "Create patient account" button is shown to Receptionists only.
- The unapplied migration was removed (no `--force`, after a read-only `migrations list` showed it Pending) and regenerated as `20261124015135_AddAccountProvisioningSecurity`; review script regenerated at `artifacts/AddAccountProvisioningSecurity.sql`. Not applied.
- Tests: `dotnet test MediCare.slnx` — 176 passed, 0 failed, 0 skipped (baseline 168; +3 SQL LocalDB tests for production Identity config and seeder behavior, +5 login-username tests). The SQL fixture used disposable LocalDB databases only. `dotnet list package --vulnerable` reported none.
- Decisions recorded in `DECISIONS.md`: queue link after grace expiry (keep null, narrow index to live attempts), attachments on disk outside `wwwroot`, analytics measured + M/G/1, dated breaks only.

### Phase 2C Step 2 — attendance attempts, consultation flow, Call Next, queue board (2026-11-24)

- Narrowed the unique index on `QueueEntries.AppointmentId` to live attempts (`Waiting`, `Called`, `InConsultation`). Scheduled check-in is refused only by a live attempt, so a cancelled attempt ("patient left") allows a later check-in of the same Confirmed appointment. Walk-ins after No-Show keep a null appointment link.
- Added `Appointment.MarkCompleted` (Confirmed only) and `ConsultationService`: the owning Doctor starts (Called → InConsultation) and completes (InConsultation → Completed) consultations; completing a scheduled entry completes its appointment in the same transaction.
- Added `ReceptionQueueService`: Call scheduled (appointment-time order, doctor must be free), Return to waiting (a walk-in then needs a new approval), Cancel waiting entry, and Call Next. Call Next first runs Grace Period start/expiry processing for that doctor and day, then calls the earliest checked-in scheduled patient, otherwise the FIFO head walk-in only when it holds a usable approval. The walk-in validation is shared with the explicit Call (`WalkInCallRules`).
- Added a queue board read model with estimated start times based on expected consultation duration (fallback: configured duration), shown as estimates. New pages: `Reception/Queue` (check-in, walk-in registration by phone, approval requests, call, call next, return, cancel) and `Doctor/Queue` (start/complete).
- Migration `20261124020835_NarrowQueueAppointmentIndexToLiveAttempts`; review script `artifacts/NarrowQueueAppointmentIndexToLiveAttempts.sql`. Not applied.
- Tests: `dotnet test MediCare.slnx` — 229 passed, 0 failed, 0 skipped (+53). Pages were compiled but not exercised in a browser.
- Follow-ups assigned to Step 3: clinic cancellation of an appointment with a Waiting queue entry cancels that entry (refused when the patient is already Called/InConsultation); cancelling a walk-in expires its Pending admission request. Scheduled calls do not apply a working-hours check (scheduled patients are inside their booked slot or grace window).

### Phase 2C Step 3 — working-hour extensions (2026-11-24)

- Added `DoctorWorkingHourExtension` (Pending/Approved/Rejected/Cancelled) with check constraints, an FK to the requesting user, and a filtered unique index allowing one live (Pending/Approved) extension per doctor/date/window end. Reception/Admin request extensions; only the owning Doctor approves or rejects; the Doctor may also record an extension of their own schedule directly (recorded approval). Past-dated Pending requests display as Expired and cannot be decided. An Approved extension cannot be cancelled while an appointment overlaps the extended part.
- Added `DoctorAvailabilityService` as the single availability source: active weekday windows with approved extensions applied, nothing on holidays, breaks and live appointments as blocked intervals, plus `GetAvailableSlotsAsync`. Booking/rescheduling, the explicit walk-in Call, and Call Next now honour approved extensions (closes the earlier "normal working hours only" limitation).
- Queue fixes: clinic cancellation/rescheduling cancels a Waiting queue entry in the same transaction and is refused when the patient is already Called/InConsultation; cancelling a walk-in withdraws its Pending admission request (recorded as Expired).
- Pages: `Doctor/Extensions` (decide requests, add own, cancel) and `Reception/Extensions` (request, cancel pending); pending-request count on `Doctor/Queue`.
- Fixed an existing SQL test that left the disposable database rolled back to an older schema after running.
- Migration `20261124022220_AddDoctorWorkingHourExtensions`; review script `artifacts/AddDoctorWorkingHourExtensions.sql`. Not applied.
- Tests: `dotnet test MediCare.slnx` — 282 passed, 0 failed, 0 skipped (+53). Pages compiled, not exercised in a browser.

### Phase 2C Step 4 — doctor leaves early / mass disruption (2026-11-24)

- Added `DoctorDisruption` (one per doctor/date), `DisruptionWorklistItem` (outcomes Pending/Contacted/Rescheduled/Cancelled) and `DisruptionContactAttempt` history, with check constraints, unique indexes and FKs. `DoctorBreak` gained a nullable `DisruptionId`.
- Reception/Admin record a disruption (doctor, date, from-time, reason). The remaining time in every effective window (approved extensions included) is blocked with dated breaks; working hours are never changed. The worklist holds affected Confirmed appointments (starting at/after the from-time or running at it) and waiting patients; a waiting scheduled entry and its appointment are one item.
- Staff log contacts, reschedule through the clinic rules (original → Rescheduled), or cancel with reason `clinic unavailable` (single constant `AppointmentCancellationReasons.ClinicUnavailable`). Recording day end cancels every unresolved item. Unresolved appointments whose start has passed are cancelled with `clinic unavailable`, never No-Show: processing runs at Call Next, on queue/disruption page loads, and from a hosted service every `Clinic:Maintenance:IntervalMinutes` (5 in appsettings, validated, no code default). Grace Period start and No-Show are skipped while a worklist item is unresolved.
- `ConfirmAsync` now re-validates availability, so a Pending request inside blocked time cannot be confirmed. `ICheckInWarningProvider` is implemented: check-in is not blocked but returns and displays a warning.
- Added `IPatientNotificationPublisher` with a no-op implementation, called after disruption cancellations, day end and worklist reschedules; Phase 3 replaces it.
- Pages: `Reception/Disruptions` and `Reception/Disruption/{id}` (worklist with contact log, reschedule using available slots, cancel, record day end); warnings on `Reception/Queue`.
- Migration `20261124024843_AddDoctorDisruptions`; review script `artifacts/AddDoctorDisruptions.sql` (additive only). Not applied.
- Tests: `dotnet test MediCare.slnx` — 325 passed, 0 failed, 0 skipped (+43). The hosted-service loop itself is not covered by an automated test. Pages compiled, not exercised in a browser.
- Open question: a patient already checked in whose appointment start has passed when the disruption is recorded is cancelled with `clinic unavailable` by the next processing run (literal rule). Confirm whether checked-in patients should instead stay open for manual handling.

### Phase 2C Step 5a — schedule management, administration, discovery (2026-11-24)

- Doctor (own schedule) and Admin (any doctor) manage working-hour windows (add, edit, activate/deactivate), appointment duration, dated breaks, holidays, and a profile biography. Invalid ranges and overlapping active windows are refused (application check plus the `TR_DoctorWorkingHours_NoActiveOverlap` trigger). Changes are refused when a future Pending/Confirmed appointment would fall outside the windows or off the duration grid, when a live extension references a changed window end, or when bookings exist in a new holiday/break. Disruption-created breaks cannot be removed.
- Admin pages: Specializations (create, rename, delete only when unused), Staff (list doctors/receptionists, deactivate/reactivate; deactivating a doctor also hides them from booking; an Admin cannot deactivate themself), Settings (minimum consultation sample size, stored in the new single-row `ClinicSettings` table; null = unset → fallback to configured duration). `ExpectedDurationService` now reads the minimum from `ClinicSettings`; the config key `Clinic:ExpectedDuration:MinimumSampleSize` is no longer used. The 60-minute cap remains a named constant.
- Inactive accounts cannot sign in (generic error message) and are signed out on their next request.
- Doctor discovery: `Doctors` (filter by specialization, name, optional date with free-slot counts) and `Doctors/Details/{id}` (profile, weekly hours, holidays, free slots with Book links). Role-aware home page.
- Migration `20261124030608_AddScheduleManagementAndClinicSettings` (adds `Doctors.Biography`, `ClinicSettings`, and the overlap trigger via `sp_executesql`); review script `artifacts/AddScheduleManagementAndClinicSettings.sql`. Not applied. The trigger statement was checked on a throwaway LocalDB database that was then dropped.
- Tests: `dotnet test MediCare.slnx` — 395 passed, 0 failed, 0 skipped (+70). Pages compiled, not exercised in a browser.
- Open questions: should deactivating a doctor cancel or flag their future appointments (currently untouched)? Concurrent overlapping window inserts surface as an error page instead of a friendly message.

### Phase 2C Step 5b — booking, appointment management, profile (2026-11-24)

- Patient pages (`Appointments/Book`, `Appointments/Mine`, `Appointments/Reschedule/{id}`): booking creates a Pending request (a failure shows the doctor's other free slots); own appointments with cancellation reasons and reschedule links; cancel and reschedule only when the rules allow (same rule helper as the server). The patient is always resolved from the signed-in account; another patient's appointment returns 404.
- Reception/Admin pages: patient search by phone (any Egyptian format) or name, patient detail, book for a patient (`BookByClinicAsync`: created Confirmed in one serializable transaction, also used for walk-in alternative appointments), day view with clinic cancel (reason required) and clinic reschedule, and an improved requests page (phone, original time for replacements). The clinic actor comes from the signed-in role, never from the form.
- `Account/Profile` for all roles: change full name (Identity user and Patient/Doctor profile in one transaction); patients add, change or remove an optional email (unique among non-null emails).
- Booking and rescheduling into a past date or an already-passed time today are refused (`AppointmentPolicy.EnsureNotInPast`).
- No model change and no migration.
- Tests: `dotnet test MediCare.slnx` — 436 passed, 0 failed, 0 skipped (+41). Pages compiled, not exercised in a browser.

### Business-logic review (2026-11-24)

- A read-only review compared the built work against `BUSINESS_IDEA.md`, `DECISIONS.md` and the specification. The core rules were confirmed. Findings verified by the orchestrator include: Approved admission permits expired with the pending window; an unapproved FIFO-head walk-in blocked approved ones; calls and walk-in registration were still possible after a doctor left early; checked-in patients were auto-cancelled during a disruption; stale previous-day consultations blocked a doctor; Grace Period expiry ran only on Call Next/check-in; booking races surfaced as error pages; cancelling an original left its pending replacement unconfirmable; no AccessDenied page; documentation contradictions. The user's decisions are recorded in `DECISIONS.md` ("Decisions after the business-logic review"). Fixes are assigned to Step 6.

### Phase 2C Step 6 — business-review fixes (2026-11-24)

- Approved admission permits stay valid for the rest of their clinic day (single use); the pending duration applies only to Pending requests.
- Walk-in calls follow FIFO among approved walk-ins only; unapproved or rejected walk-ins keep their position without blocking. Call Next reports how many walk-ins still need approval.
- From a disruption's from-time, and during dated breaks, that doctor cannot be called for and new walk-ins cannot be registered (`DoctorCallWindowService`).
- Checked-in patients are no longer auto-cancelled during a disruption; day end still cancels everything unresolved. Worklist items gained `ResolvedAtUtc`/`ResolutionNote` and close automatically when their appointment or queue entry is no longer live.
- GET pages no longer change state. The maintenance hosted service now runs three isolated tasks: disruption processing, Grace Period start/No-Show for every active doctor, and a stale-entry sweep (previous-day Waiting/Called → Cancelled; InConsultation → Completed). Walk-in registration runs Grace processing first.
- Appointment operations run through the serializable retry wrapper; a final unique-index conflict becomes a friendly message instead of an error page.
- Cancelling an original also cancels its pending replacement; a replacement cannot be confirmed once the original's day has arrived; clinic cancel/reschedule refuse past-dated appointments.
- A patient cannot hold two overlapping live appointments (application rule under serializable isolation).
- Deactivating a doctor (with a confirmation page showing counts) cancels their future appointments with reason `doctor no longer available`, cancels waiting entries, and notifies each patient; refused while one of their patients is Called/InConsultation.
- Added an AccessDenied page and explicit login/access-denied cookie paths.
- Migration `20261124034947_ReviewFixes`; review script `artifacts/ReviewFixes.sql` (two nullable columns, a backfill, two check constraints). Not applied.
- Tests: `dotnet test MediCare.slnx` — 483 passed, 0 failed, 0 skipped (+47).
- Follow-up: Confirmed appointments from previous days with no attendance are not yet resolved (Grace processing covers the current day only); assigned to the next step.

### Phase 2 commit and push (2026-11-24)

- With the user's approval, committed Phase 2 (2B round 3 through 2C-6) as `f07350c` on branch `Nour` and pushed to `origin/Nour` as a fast-forward from `f8e1375` (no force, no merge, no other branch touched). No migration was applied.

### Phase 3 Step 3a — medical records, prescriptions, history (2026-11-24)

- Previous-day Confirmed appointments are now resolved by the maintenance job: open disruption item → cancelled `clinic unavailable`; never checked in → No-Show; checked in but never completed → cancelled `patient left before consultation`.
- Added `MedicalRecord` (one per attendance attempt; chief complaint, diagnosis, notes), `MedicalRecordAddendum`, `Prescription` (one per record) and `PrescriptionItem` (medication, dosage, frequency, duration in days, instructions), with check constraints and unique indexes. Only the attempt's doctor creates the record, only for an InConsultation/Completed entry; the author may edit the record and prescription on the visit day only; afterwards corrections are addenda by a treating doctor.
- `MedicalRecordAccessPolicy`: patients see their own records; doctors see records of patients they have a treating relationship with; Receptionist and Admin have no access to medical content (the specification's Admin "policy-gated" access has no defined policy — open item). Refused reads return 404. Medical pages are served with `Cache-Control: no-store`.
- Pages: doctor visit record and prescription editor (from `Doctor/Queue`), doctor patient-history view, patient medical history and active prescriptions, printable prescription.
- Migration `20261124040738_AddMedicalRecordsAndPrescriptions`; review script `artifacts/AddMedicalRecordsAndPrescriptions.sql`. Not applied.
- Tests: `dotnet test MediCare.slnx` — 540 passed, 0 failed, 0 skipped (+57). Pages compiled, not exercised in a browser.

### Phase 3 Step 3b — medical attachments and in-app notifications (2026-11-24)

- Added `MedicalAttachment` (PDF/JPEG/PNG; metadata only in the database) and a filesystem store under `App_Data/medical-files`, resolved against the content root and refused at startup if inside `wwwroot` (`App_Data/` is git-ignored). File type is decided by magic bytes; size limit `MedicalFiles:MaxFileSizeBytes` (10 MB); server-generated GUID file names with traversal checks; SHA-256 recorded; the file is deleted if the database write fails. A treating doctor uploads at any time; downloads re-check `MedicalRecordAccessPolicy` and are served as attachments with `nosniff` and `no-store`. No deletion (append-only records).
- Added `Notification` (per-recipient, read state, optional dedup key with a filtered unique index), `NotificationService`, and an `INotificationProvider` abstraction with the in-app provider. The no-op publisher was replaced; publishing happens after commit and a failure never fails the business operation. Patient notices: booking confirmed/rejected, reschedule confirmed/rejected, clinic cancel (with reason), clinic reschedule, disruption and deactivation cancellations, and a day-before reminder sent by the maintenance job (idempotent). Clinician notices: walk-in admission request and extension request to the doctor; extension decision to the requesting Reception user. Bell with unread count and a Notifications page.
- The doctor visit form now keeps typed values when validation fails.
- `NoOpPatientNotificationPublisher.cs` is no longer used; it was left in place pending approval to delete a committed file.
- Migration `20261124042734_AddMedicalAttachmentsAndNotifications`; review script `artifacts/AddMedicalAttachmentsAndNotifications.sql`. Not applied.
- Tests: `dotnet test MediCare.slnx` — 630 passed, 0 failed, 0 skipped (+90). The app was not started (startup seeding writes to the configured database), so pages and download headers were not checked in a browser.

### Phase 3 commit and push (2026-11-24)

- With the user's approval, committed Phase 3 as `186440e` on `Nour` and pushed to `origin/Nour` as a fast-forward from `f07350c`. Verified afterwards that `origin/Nour` equals local `HEAD`.

### Phase 4 — analytics and queueing-theory dashboard (2026-11-24)

- Pure calculation engine (`MediCare.Application/Analytics`): open time T from effective windows (approved extensions included) minus breaks and holidays, clipped to now for live; measured N, n, λ, μ, ρ, Wq, Ws, utilization; time-average Lq/Ls by event sweep with a Little's Law check; M/G/1 Pollaczek–Khinchine reference only when ρ < 1 and n ≥ 2 (Overloaded / InsufficientData otherwise); exclusions counted (`clinic unavailable`, invalid timestamps, service ≥ 60-minute cap); walk-in throughput; hourly profile of scheduled vs actual arrivals in clinic-local hours (DST-safe).
- No-show rate = NoShow / (Completed + NoShow): the orchestrator corrected the initial definition, which also counted never-confirmed Pending requests and unresolved Confirmed appointments, and updated the test and `docs/ANALYTICS_MODEL.md`.
- `AnalyticsService` with AsNoTracking projections and a 5-minute cache for closed periods only; historical periods must end before today; live is today only and never cached.
- Pages (inline SVG, no chart library): Admin `Analytics` (clinic table + doctor drill-down), Doctor "My statistics" (own doctor only), Reception/Admin live board (refresh button, no polling).
- Reception/Admin clinic bookings now notify the patient.
- Fixed a time-dependent SQL test that failed after 09:20 local time (clock pinned to the booking day).
- Assumptions documented in `docs/ANALYTICS_MODEL.md` (independent single-server doctors; Poisson assumption of M/G/1 is violated by scheduled arrivals, so theoretical values are a reference baseline; measured metrics are authoritative).
- Migration `20261124105540_AddAnalyticsIndexes` (covering index on `QueueEntries (QueueDate, DoctorId)`); review script `artifacts/AddAnalyticsIndexes.sql`. Not applied.
- Tests: `dotnet test MediCare.slnx` — 669 passed, 0 failed, 0 skipped (+39). Pages compiled, not exercised in a browser.

### Documentation refresh (2026-11-24)

- Rewrote `PROGRESS.md` (47/47) and `PROJECT_CONTEXT.md` to the current state; updated status labels, test count, queue index and entity list in `TECHNICAL_SPECIFICATION.md`; replaced stale sections of `DECISIONS.md` (attempt correction, open decisions, implementation boundary). Read-only `dotnet ef migrations list`: 2 applied, 10 pending.

### Local database and browser check (2026-11-24)

- With the user's approval, applied all pending migrations to the configured local development database (LocalDB `MediCare`, verified empty beforehand: 0 users, patients, doctors, appointments). `dotnet ef migrations list`: 0 pending. Bootstrap Admin credentials were generated and stored in .NET User Secrets only (not in the repository).
- Browser check (`http://localhost:5279`): Admin sign-in; specialization, doctor and receptionist creation (temporary passwords shown once); doctor schedule window; patient self-registration without email; patient sign-in with a local-format phone (`010…`) and with `+20 …`; doctor search and slot list (current Cairo time respected); patient booking (Pending); forced first-login password change for Receptionist and Doctor; Reception confirm; Reception creating a phone-only patient; queue check-in; walk-in registration by phone; admission request → doctor notification → approval; Call Next (scheduled first, then the approved walk-in); start/complete consultation; visit record; prescription save and replace; attachment upload, download headers (`attachment`, `nosniff`, `no-store`), storage under `App_Data` (git-ignored), rejection of a disguised executable; patient history and active prescription; patient notification; patient refused on doctor pages (AccessDenied page); live analytics board.
- Bugs found and fixed:
  - Saving a prescription returned HTTP 500 (`DbUpdateConcurrencyException`): EF treated new children added through a tracked record's navigations (prescription, items, addenda, attachments) as existing rows because of their client-generated Guid keys. All single-Guid domain keys are now `ValueGeneratedNever`; prescription items use `ClientCascade` so a replaced prescription deletes its old items. Migration `20261124112210_ClientGeneratedKeys` (recreates the items FK with NO ACTION; review script `artifacts/ClientGeneratedKeys.sql`), applied locally. Regression test against LocalDB added.
  - Several pages showed UTC times (walk-in requests, notifications, attachments, addenda, extensions, disruptions, settings, live board). Added `ClinicTime.Format` and an injected clinic time zone; all displays are clinic-local.
- UI fixes: prescription inputs have accessible labels; "Full name" / "Remember me" labels; navbar "Sign in" (not "Staff sign in"); 24-hour times on the requests page; navbar collapses below 1200 px so the Admin menu no longer wraps.
- Tests: `dotnet test MediCare.slnx` — 672 passed, 0 failed, 0 skipped (+3).
- Not yet clicked through: Admin analytics, Doctor statistics, extensions, disruption worklist.

### Browser check, part 2 (2026-11-24)

- Exercised the remaining pages: Admin analytics (empty history shows "Insufficient data"; open time 44 h for four Tuesdays) and doctor drill-down (metrics, Little's Law check, hourly chart); Doctor statistics; Doctor recording an own extension (slots extended to 20:40 immediately); Reception requesting an extension for 2026-12-01 (Pending, no effect on slots) and the Doctor approving it (slots extended to 21:00); Reception booking-and-confirming for a patient; recording a doctor-leaves-early disruption from 15:30 (slots blocked from 15:20, including the extended hour); worklist contact log with history; rescheduling the affected appointment from the worklist (original → Rescheduled, patient notified); recording day end behind its confirmation dialog.
- Fixed: `Doctor/Statistics` returned HTTP 500 because the shared analytics view used relative partial names that do not resolve from `DoctorController` (now full paths); analytics range errors showed the framework's "(Parameter 'toDate')" suffix (`AnalyticsErrorText`); extension redirects put a culture-formatted date in the URL (now `yyyy-MM-dd`).
- Known limitation recorded in `DECISIONS.md`: analytics open time for past dates uses the doctor's current working windows (schedule changes are not effective-dated).
- Minor, not changed: after a worklist reschedule, the "Last contact" column still shows the previous contact note.
- Tests: `dotnet test MediCare.slnx` — 674 passed, 0 failed, 0 skipped (+2).

### Account recovery, sign-in hardening, page-level tests (2026-11-24)

- Password reset (approved decisions in `DECISIONS.md`): Admin resets Doctor/Receptionist passwords (never an Admin or their own account); Receptionist or Admin resets Patient passwords after verifying identity in person. A one-time temporary password is shown with `no-store`, `MustChangePassword` is set, lockout is cleared, and the security stamp changes. The session guard now compares the cookie's security stamp with the account's on every request (the user row was already loaded), so a reset or password change ends other sessions immediately instead of after Identity's 30-minute re-validation.
- Identity: minimum password length 12; lockout after 5 failures for 15 minutes, including new users; login keeps the generic error message.
- Rate limiting (built-in): `auth` (POST login and change password, 10 per 60 s per IP) and `register` (5 per hour per IP), limits in `appsettings.json`, HTTP 429 with a friendly page and `Retry-After`.
- Security headers on every response (CSP without inline script, nosniff, X-Frame-Options DENY, Referrer-Policy, Permissions-Policy); auth cookie HttpOnly, SameSite=Lax, Secure outside Development. Inline handlers moved to `site.js` data attributes (`data-confirm`, `data-print`, `data-close-window`); regression tests scan views for inline script and remote scripts.
- Browser check found that `_PrintLayout` did not load `site.js`, so the prescription Print/Close buttons stopped working after the CSP change; fixed, with a test that every layout loads `site.js`.
- Page-level integration tests with `Microsoft.AspNetCore.Mvc.Testing` (approved, test project only): the real application against a disposable LocalDB database; per-role smoke tests for every main page (allowed → 200, anonymous → login, wrong role → AccessDenied); security headers and no inline script in rendered HTML; regressions (visit record and prescription save/resave, Doctor/Statistics, registration without email + local-phone login, cross-patient access); password reset end to end including immediate sign-out; rate limiting (429); attachment upload/download headers and access; clinic-local time rendering. 53 tests, about 10 s.
- Removed an unused controller parameter and fixed four nullability/xUnit analyzer warnings; full rebuild has 0 warnings.
- Test-data note: during its first run the integration-test subagent deleted `MediCare.Web/App_Data/medical-files`, including the earlier browser-test attachment `ecg-report.pdf` (test data only); its metadata row remains, so that one download now returns 404. The tests now use a temporary folder.
- The local `MediCare` database was verified unchanged after the test runs; no test databases were left behind.
- Tests: `dotnet test MediCare.slnx` — 765 passed, 0 failed, 0 skipped.

### Queue analytics review, phase 1 (2026-11-28)

- Review of the statistics and queueing model written to `docs/QUEUEING_MODEL_REVIEW.md` (Arabic): formulas verified against simulation, 12 findings, 3-phase plan. Phase 1 (no migrations) implemented:
  - Little's Law line uses the served throughput `n/T`, the same as its percentage (before, the line used `N/T` and could show two different numbers next to "0% apart"). λ and ρ stay arrivals-based (decision in `DECISIONS.md`).
  - New `Not seen` (attempts ended Cancelled or NoShow) as a count and share on the doctor page and a column in the clinic table.
  - Busy time counts long consultations up to the cap instead of dropping them; mean and variance still exclude them.
  - The Little's Law section no longer claims to check timestamp accuracy: the gap is the waiting outside open hours, and the page shows arrivals outside open time.
  - Waits for booked patients and walk-ins separately, and the delay past the booked time (waiting after an early arrival is not counted). `AnalyticsVisitRow` carries the appointment date and start time.
  - Near-saturation warning above ρ = 0.85 (`MG1Model.NearSaturationRho`), the consultation standard deviation, and a "small sample" note below 20 observations.
- Definitions updated in `docs/ANALYTICS_MODEL.md`.
- Tests: `dotnet test MediCare.slnx` — 894 passed, 0 failed, 0 skipped (+9 analytics tests). Pages rendered by the page-level tests; not checked visually in a browser.

### Queue analytics review, phase 2 (2026-11-28)

- Open time T includes work the doctor demonstrably did outside the schedule (`OpenTimeCalculator.IncludeWork`: capped consultation intervals plus day edges). New `ScheduledOpenHours`. The review's case 4 now shows 100% time with patients instead of 125%, but still Overloaded (ρ = 1.0) for a day worked straight through with nobody waiting; whether that should count as overload is open.
- Disruptions needed no change: recording one already creates dated breaks for the rest of the day, so T excluded that time. The review's problem 2 item 1 was wrong and is corrected there.
- Kingman G/G/1 (`KingmanModel`) next to M/G/1, with the arrival variability Ca² from same-day gaps; equals M/G/1 at Ca² = 1.
- Hourly workload (arrivals per open hour × mean consultation) in the hourly table, red at 85% or more.
- Live board: "Last hour" arrival rate, and the consultation mean blended with the last 30 days (weight up to 5 consultations, `GetServiceHistoryAsync`), so the workload is available from the first arrival.
- Queue board estimates: walk-ins count booked patients who have not arrived yet (NotArrived, GraceRunning) and are due before their turn; an 80% range (±1.28 σ √m, σ from `ExpectedDurationResult.StdDevMinutes`); a running consultation uses the mean remaining time of longer past consultations.
- Board estimates and analytics views were implemented by Haiku subagents and reviewed here.
- Tests: `dotnet test MediCare.slnx` — 911 passed, 0 failed, 0 skipped (+17). The queue board and analytics pages were rendered by the page-level tests; not checked in a browser.

### Queue analytics review, phase 3 (2026-11-28)

- Migration `20261127224403_AddQueueEntryClosedAtAndWorkingHourSnapshots` (additive: nullable `QueueEntries.ClosedAtUtc`, new table `DoctorWorkingHourSnapshots`); review script `artifacts/AddQueueEntryClosedAtAndWorkingHourSnapshots.sql`. Not applied to the local or live database. The app does not migrate on startup, so the script must run before this code runs against a database.
- Working-hour history: the schedule service writes a snapshot on every successful add, time change, deactivation and reactivation (effective from the clinic's today), with a baseline of a legacy window's old state on its first change. Past open time uses the snapshot in force each day; windows with no snapshot keep the old behaviour. `DoctorWorkingHours` and its unique index are unchanged.
- Leave time: `QueueEntry.Cancel(closedAtUtc)` records when a patient left; stale closes by maintenance leave it null. New metrics: waiting-room and in-clinic averages including people who left (and, live, people still waiting), left-after-waiting count and mean wait before leaving. The Little's Law pair is unchanged.
- Priority model (Cobham, `PriorityQueueModel`) next to M/G/1 and Kingman: expected waits for booked patients and walk-ins; tested against the conservation law.
- Implemented by Haiku subagents (schema, model, analytics wiring, docs) and reviewed here; the same-day snapshot test was strengthened and checked against a mutation.
- Tests: `dotnet test MediCare.slnx` — 951 passed, 0 failed, 0 skipped (+40). The page-level tests migrate a disposable LocalDB database, so the new migration was applied there. Pages not checked in a browser.
