# MediCare – Claude Code Instructions

Clinic management system (ASP.NET Core MVC, EF Core, SQL Server, Razor + Bootstrap 5, Clean Architecture, Repository + Unit of Work).
Core goal: **minimize patient waiting time while maximizing patients served**, without breaking appointment fairness or doctor authority.

## Source of truth (read before any work)

1. `docs/Business Idea.txt` – business vision and rules (wins on any business question).
2. `docs/Technical Specification.txt` – architecture, entities, phases, restrictions.
3. `DECISIONS.md` – every confirmed decision (wins over the spec where they differ).
4. `PROJECT_CONTEXT` and `WORK_LOG` – current state and history.

If code, spec and DECISIONS disagree, stop and ask. Never invent an unresolved business rule: write the question down and wait.

## How we work

- Work proceeds in phases (1 Foundation → 2 Appointments & Queue → 3 Medical → 4 Analytics). One step at a time; each step needs my explicit approval before the next.
- Before coding a step: inspect the existing code, list gaps, propose a plan, **wait for approval**.
- After each step, report: what was done, files created/modified, behavior affected, tests run + results, known limitations, open decisions, whether DB changes are needed.
- After an approved decision, append it to `DECISIONS.md`; after each step, update `WORK_LOG`.

## Hard restrictions (need my explicit approval)

- No applying migrations or touching any real database. Migrations are generated as **SQL script only** for review. Constraint tests run only on a disposable LocalDB database that is dropped afterward.
- No git push / pull / merge / force-push / history rewrite. Remote work goes to the **`Nour`** branch only. Never stage unrelated changes; check for secrets before committing.
- No deleting files/data, no restructuring architecture, no new libraries without checking what already exists.
- Don't claim something works without running tests. Don't start the next phase/step on your own.

## Current state (as of 2026-11-24)

- Phase 1: done. Phase 2: in progress.
- **2A** (appointments, reschedule, cancellation, disruption flow, InitialCreate migration): accepted. SQL Server constraint tests passed on disposable LocalDB.
- **2B** account provisioning: round 2 reported done.
  - Admin creates Doctor + Receptionist accounts; Reception creates Patient accounts; public patient self-registration (phone required, email optional, own password).
  - Egyptian phones normalized to `+20…` and used as username; system-generated temp password shown once; persisted `MustChangePassword` + redirect middleware; first Admin only from secure config (no defaults).
  - `Patient.ApplicationUserId` required (keeps the global unique phone index). `Pkcs` pinned to 9.0.21.
  - Migration `AddAccountProvisioningSecurity`: SQL script only, **not applied**.
- **Test baseline: 168 passing.** Any change must keep these green.
- `main` branch was reviewed: reuse only UI / Identity / admin / specialization patterns. Ignore its scheduling, queue, DbContext and migrations.

### Open items before / during 2C

- **Must fix before 2C:** unique index on `QueueEntry.AppointmentId` conflicts with "one QueueEntry per attendance attempt" (appointment nullable, multiple attempts allowed).
- Next: review 2B round 2, then step 2C (check-in, scheduled queue, walk-in FIFO, Grace Period, doctor approval, working-hour extensions).

## Key confirmed decisions (summary – full list in DECISIONS.md)

**Queue**
- One `QueueEntry` per attendance attempt; `AppointmentId` nullable. `QueueSequence` is walk-in only.
- Active-consultation constraint covers `InConsultation` only.
- Walk-in FIFO: `ORDER BY ArrivedAt, QueueSequence`. Scheduled patients ordered by appointment time.

**Durations & Grace Period**
- Each doctor has a configured appointment duration (drives slot generation) and an **expected duration** = historical average (drives patient-facing timeline estimates).
- Expected duration: 1-month rolling window, configurable minimum sample size (no hardcoded value); exclude outliers (0 min, ≥ 60 min, incomplete/invalid timestamps).
- Grace Period is per doctor = **3 × expected consultation duration**. μ is the service rate, not a duration.
- Timestamps stored in UTC; day boundaries in Africa/Cairo (configurable).

**Appointments**
- Booking requires a patient account (self-registered or created by Reception).
- Appointment day starts 00:00 Africa/Cairo; patients cannot cancel from then. Reception/Admin may cancel same day.
- Patient reschedule requests follow the same before-the-day cutoff.
- Reschedule: original stays Confirmed (slot held) until the replacement is Confirmed, then original → `Rescheduled` in one transaction; untouched if replacement is rejected/cancelled. Overlap checks ignore the linked original. Uniqueness counts only live replacements. Replacement with same doctor/date/start as its original is rejected.
- Reception/Admin-booked replacements are created directly `Confirmed` (or `Cancelled` with reason if no slot).
- Unique slot index prevents double booking; End > Start check constraints. `Specializations` is a table with required FK from Doctors.

**Clinic disruption (doctor leaves early, etc.)**
- In scope. Only Reception and Admin can record it; recorded **per doctor**, not clinic-wide.
- Reception/Admin contact affected patients one by one to reschedule. When Reception records day-end with unresolved appointments, the system auto-notifies each affected patient as well.
- Unreached patients → `Cancelled` with reason "clinic unavailable" (never No-Show; excluded from queueing-theory data).

**Accounts**
- Admin creates staff accounts and links Doctor profiles. Reception-created patient accounts use phone as username, forced password change on first login, patient may add email later.

## Environment

- Local repo: `E:\train` (local baseline commit `6bffff0`).
- Remote: https://github.com/Georgette32/-MediCare-Clinic-Management-Appointment-Booking-System.git — has branches `main`, `Alaa`, `Nour`, `Nour-backup`, `Rehab`, `georgette`, `mohamed`. Our work goes to `Nour`.
- Communicate with me in Egyptian Arabic; code, docs and commits in English.
