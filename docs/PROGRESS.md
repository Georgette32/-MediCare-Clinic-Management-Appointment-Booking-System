# MediCare Progress

Checklist of the phases in `TECHNICAL_SPECIFICATION.md` against the repository. A checked item was confirmed in code and by the automated test suite on 2026-11-24. Browser (end-to-end) checks have not been done yet.

## Current state snapshot (2026-11-24)

- Branch `Nour`: Phase 2 `f07350c`, Phase 3 `186440e`, Phase 4 + documentation `e402336`, all pushed to `origin/Nour`.
- Target framework `net10.0`; SDK `10.0.401` (`global.json`); EF Core / Identity `10.0.0`; installed `dotnet-ef` CLI `11.0.0`.
- `dotnet build MediCare.slnx`: 0 errors, 0 warnings. `dotnet list package --vulnerable`: none (Pkcs pinned to 9.0.21).
- `dotnet test MediCare.slnx`: **765 passed, 0 failed, 0 skipped** (SQL tests use a disposable LocalDB database).
- Local development database (LocalDB `MediCare`): all 13 migrations applied on 2026-11-24 with the user's approval, through `20261124112210_ClientGeneratedKeys`. SQL review scripts are in `artifacts/`.
- Browser check on 2026-11-24: all role flows pass, including analytics, extensions and disruptions (see WORK_LOG); five issues found and fixed.

## Phase 1 — Foundation & Core Architecture (12/12)

- [✓] Inspect existing solution; verify Clean Architecture boundaries; review entities, Identity/roles, DbContext.
- [✓] Establish missing core domain structures; validate DI; confirm relationships.
- [✓] Acceptance: architecture consistent; functionality preserved; core domain established; no unintended schema changes.

## Phase 2 — Appointment & Queue Management (16/16)

- [✓] Appointment booking (patient Pending; Reception/Admin booking Confirmed), confirmation/rejection.
- [✓] Cancellation (patient before the day; clinic same day with reason) and rescheduling (patient and clinic, atomic).
- [✓] Doctor scheduling: working hours, duration, dated breaks, holidays, validation against existing bookings.
- [✓] Scheduled check-in; one queue entry per attendance attempt (live-attempt index).
- [✓] Scheduled ordering; Call, Call Next, consultation start/complete; queue boards with estimates.
- [✓] Walk-in FIFO registration with daily sequence; FIFO among approved walk-ins.
- [✓] Grace Period (3 × expected duration, frozen, anchored to scheduled start, DST-safe), No-Show processing in the maintenance job.
- [✓] Per-entry Doctor approval for walk-ins (permit valid for the clinic day, single use).
- [✓] Working-hour extensions with Doctor approval, integrated into availability, booking and calls.
- [✓] Doctor-leaves-early workflow: blocking, worklist, contact log, `clinic unavailable` cancellation, day end, check-in warning.
- [✓] Accounts: Admin staff provisioning, Reception and self-registered patients, phone normalization, first-login password change, bootstrap Admin, deactivation.
- [✓] Acceptance: double booking prevented (index + serializable check + retry).
- [✓] Acceptance: scheduled ordering and Call Next priority.
- [✓] Acceptance: walk-in registration and FIFO.
- [✓] Acceptance: Grace Period workflow and guards.
- [✓] Acceptance: Doctor approval and ownership enforced.

## Phase 3 — Medical Operations (8/8)

- [✓] Medical records (one per attendance attempt; editable on the visit day, addenda afterwards).
- [✓] Prescriptions (multi-item, active window, printable).
- [✓] Medical attachments (PDF/JPEG/PNG, stored outside `wwwroot`, authorized download).
- [✓] Patient medical history and active prescriptions.
- [✓] Notifications (in-app, provider abstraction, reminders, staff notices).
- [✓] Acceptance: medical information is associated with the patient and attendance attempt.
- [✓] Acceptance: unauthorized medical-data access is prevented (`MedicalRecordAccessPolicy`).
- [✓] Acceptance: medical workflows covered by service and authorization tests.

## Phase 4 — Analytics & Dashboard (11/11)

- [✓] Operational queue data collection (arrival, call, start, completion timestamps).
- [✓] Doctor-specific arrival and service statistics.
- [✓] Queueing model selection: measured metrics + M/G/1 (Pollaczek–Khinchine) reference.
- [✓] `Ls`, `Lq`, `Ws`, `Wq` calculations (measured, Little's Law check, theoretical when ρ < 1).
- [✓] Working-hours graph (scheduled vs actual arrivals per clinic-local hour, inline SVG).
- [✓] Dashboard integration (Admin clinic analytics, Doctor "My statistics", Reception live board).
- [✓] Performance: AsNoTracking projections, covering index on `QueueEntries (QueueDate, DoctorId)`, short cache for closed periods.
- [✓] Acceptance: metrics mathematically consistent (hand-computed tests).
- [✓] Acceptance: model assumptions documented (`docs/ANALYTICS_MODEL.md`).
- [✓] Acceptance: historical and live observations distinguishable.
- [✓] Acceptance: dashboard shows operational KPIs (Wq, Ws, Lq, ρ, utilization, no-show rate, walk-in throughput, overload).

## Overall checklist

**47/47 checked.** Phase 1 12/12, Phase 2 16/16, Phase 3 8/8, Phase 4 11/11.

## Resume here

1. Before any non-local deployment, review the SQL scripts in `artifacts/` in migration order.
3. Open decisions: see "Open / decision required" in `docs/DECISIONS.md`.
4. Known tooling note: `dotnet-ef` CLI is 11.0.0 while packages are 10.0.0; align when tooling is next updated. `artifacts/InitialCreate.sql` is stale (ProductVersion 11.0.0).

## Rules reminder

- Present a plan and wait for approval before a new phase or scope.
- Do not apply a migration or run Git pull/merge/force operations without explicit approval; push only to `Nour`.
