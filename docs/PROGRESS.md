# MediCare Progress

This checklist maps the work in `Technical Specification.txt`, Section 17, to the current repository. A checked item was confirmed in code and, where applicable, tests on 2026-11-23. Phase counts include both implementation bullets and acceptance criteria.

## Current state snapshot

- Target framework: `net10.0` across the solution.
- SDK: `10.0.401` (pinned in `global.json`).
- EF Core / ASP.NET Core Identity: `10.0.0`.
- Installed `dotnet-ef`: `11.0.0`.
- Latest `dotnet build MediCare.slnx`: succeeded, 0 errors, 12 NU1902/NU1903 warning instances for `System.Security.Cryptography.Pkcs` 9.0.4.
- Latest `dotnet test MediCare.slnx`: 126 total, 126 passed, 0 failed, 0 skipped. SQL integration tests use a disposable LocalDB fixture.
- Read-only `dotnet ef migrations list` against the configured database:
  - `20261121190210_InitialCreate` — Applied
  - `20261123000418_InitialCreate_net10` — Applied
  - `20261123020059_AddWalkInAdmissionApprovals` — Pending; generated but NOT applied.
- No `dotnet ef database update` was run. The pending migration must be reviewed and applied by the user.

## Phase 1 — Foundation & Core Architecture (12/12)

- [✓] Inspect existing solution.
- [✓] Verify Clean Architecture boundaries.
- [✓] Review existing entities.
- [✓] Review Identity and roles.
- [✓] Review DbContext and configurations.
- [✓] Establish missing core domain structures.
- [✓] Validate dependency injection.
- [✓] Confirm database relationships.
- [✓] Acceptance: architecture is consistent.
- [✓] Acceptance: existing functionality is preserved by the implemented workflows and tests.
- [✓] Acceptance: core domain model is established and reviewed.
- [✓] Acceptance: no unintended schema changes; the only unapplied schema delta is the generated, review-pending approval migration.

## Phase 2 — Appointment & Queue Management (15/16)

- [✓] Appointment booking.
- [✓] Appointment cancellation.
- [✓] Rescheduling.
- [✓] Doctor scheduling validation against configured hours, breaks, and holidays.
- [✓] Scheduled patient check-in and queue entries.
- [✓] Scheduled ordering query by appointment time.
- [✓] Walk-in FIFO registration and per-doctor/date sequence under serializable concurrency control.
- [✓] Grace Period start, freeze, expiry, No-Show processing, and scheduled-start UTC anchor with DST handling.
- [✓] Per-entry Doctor approval is enforced for walk-in Call.
- [ ] Working-hour extensions.
- [✓] Acceptance: double booking is prevented.
- [✓] Acceptance: scheduled ordering query returns scheduled-time order. Call Next orchestration is still pending.
- [✓] Acceptance: walk-in registration and FIFO ordering work.
- [✓] Acceptance: Grace Period workflow and guards work.
- [✓] Acceptance: Doctor approval and ownership checks are enforced.
- [✓] Acceptance: critical queue tests pass in the latest full-suite run.

## Phase 3 — Medical Operations (0/8)

- [ ] Medical records.
- [ ] Prescriptions.
- [ ] Medical attachments.
- [ ] Patient medical history.
- [ ] Notifications.
- [ ] Acceptance: medical information is correctly associated with patients.
- [ ] Acceptance: unauthorized medical-data access is prevented.
- [ ] Acceptance: medical workflows are functional.

## Phase 4 — Analytics & Dashboard (1/11)

- [✓] Operational queue data collection (arrival, call, consultation-start, and completion timestamps).
- [ ] Doctor-specific arrival and service statistics.
- [ ] Queueing Theory model selection.
- [ ] `Ls`, `Lq`, `Ws`, and `Wq` calculations.
- [ ] Working-hours graph.
- [ ] Dashboard integration.
- [ ] Performance optimization.
- [ ] Acceptance: metrics are mathematically consistent.
- [ ] Acceptance: model assumptions are documented.
- [ ] Acceptance: historical and live observations are distinguishable.
- [ ] Acceptance: dashboard provides useful operational insights.

## Overall checklist

**28/47 checked; 19 remain unchecked.** Phase counts: Phase 1 12/12, Phase 2 15/16, Phase 3 0/8, Phase 4 1/11.

## Recent completed work and verification milestones

- **Phase 2B stabilization:** fixed Grace Period result-property references in tests; added start-before-scheduled-time and current-clinic-date guards; fixed Expected Duration tests to set sample-size options and added the unset-sample fallback case; updated migration-count expectations. An earlier full run recorded in the work log was 83 total, 79 passed, 4 failed. The exact isolated full-suite count immediately after those fixes was not retained; all fixes are covered by the passing cumulative runs below.
- **Grace Period scheduled-time anchor and DST:** stored the scheduled start instant rather than processor time; retained inclusive expiry (`now >= scheduled start + frozen minutes`); added Cairo transition rule tests for ambiguous and nonexistent times. Covered by the next complete suite milestone.
- **Phase 2B Check-In Service:** scheduled check-in validates appointment, doctor/date, duplicate status, and grace expiry; walk-in registration uses per-doctor/date FIFO sequence and bounded SQL retries; Reception/Admin endpoints enforce server authorization. The subsequent complete suite milestone covered this work.
- **Phase 2C Step 1, initial approval implementation:** the full suite recorded 123 total, 123 passed, 0 failed, 0 skipped.
- **Approval constraint hardening:** regenerated the unapplied migration, Designer, snapshot, and SQL script; added direct insert checks for invalid and valid decision/timestamp combinations and the filtered unique index. Full suite: 126 total, 126 passed, 0 failed, 0 skipped.
- **This session:** build succeeded and the full test suite passed again with 126 total, 126 passed, 0 failed, 0 skipped.

## Resume here

### Immediate next actions

1. I review `artifacts/AddWalkInAdmissionApprovals.sql` and apply the migration myself.
2. After applying it, run `dotnet ef migrations list` and confirm three migrations with no Pending marker.
3. Implement Call Next: serve scheduled patients first, then approved walk-ins FIFO. This closes Scenario A and the walk-in replacement queue item.
4. Implement Working-Hour Extensions.
5. Implement disruption handling and the check-in warning provider.
6. Complete Reception/Doctor UI beyond the current request/approval endpoints and Doctor pending list.

### Open items

- Resolve the NuGet vulnerability warnings for `System.Security.Cryptography.Pkcs` 9.0.4, including a High advisory, as a separate task before deployment.
- The installed `dotnet-ef` CLI is 11.0.0 while EF packages are 10.0.0. Use/install the matching 10.x tool in a separately approved task; do not install it as part of this work.
- `artifacts/InitialCreate.sql` is stale (`ProductVersion` 11.0.0); regenerate it when a migration is next approved.
- Development configuration specifies `Africa/Cairo`; the deployed environment's `Clinic:TimeZoneId` value has not been verified.

### Rules reminder

- Present a plan and wait for approval before implementing a new phase or scope.
- Do not apply a migration, or perform Git pull/merge/force operations, without explicit approval.
