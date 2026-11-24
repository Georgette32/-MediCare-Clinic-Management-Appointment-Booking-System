# MediCare Project Context

## Current state (2026-11-24)

- ASP.NET Core MVC clinic system, `net10.0`, EF Core / Identity 10.0.0, SQL Server, Razor + Bootstrap 5, Clean Architecture (Domain, Application, Infrastructure, Web, Tests).
- Phases 1–4 are implemented and covered by 669 automated tests. Checklist: `docs/PROGRESS.md`; history: `docs/WORK_LOG.md`; confirmed rules: `docs/DECISIONS.md`; analytics model: `docs/ANALYTICS_MODEL.md`.
- Only the first two migrations are applied to the configured local database; the later ten are generated with SQL review scripts in `artifacts/` and wait for the user's review.

## What the system does

- **Accounts and roles:** independent Admin, Doctor, Receptionist and Patient roles. Admin provisions Doctor/Receptionist accounts; Reception creates patient accounts; patients may self-register (Egyptian mobile number as username, optional email). Temporary passwords are shown once and must be changed at first sign-in. The first Admin comes from secure configuration only. Admin can deactivate staff; deactivating a doctor cancels their future appointments and notifies patients.
- **Discovery and scheduling:** patients and staff find doctors by specialization, name and date availability. Doctors (and Admin) manage weekly working windows, appointment duration, dated breaks, holidays and a profile; changes that would break existing bookings are refused.
- **Appointments:** patient requests stay Pending until Reception confirms; Reception/Admin bookings are Confirmed directly. Double booking and overlapping bookings for one patient are prevented. Patients cancel/reschedule only before the appointment's clinic-local day; Reception/Admin may cancel the same day with a reason and reschedule atomically.
- **Queue:** scheduled check-in and walk-in FIFO registration; one queue entry per attendance attempt; Call / Call Next serve checked-in scheduled patients by appointment time, then approved walk-ins in FIFO order; the doctor starts and completes consultations. Grace Period = 3 × expected consultation duration (frozen, anchored to the scheduled start); expiry → No-Show. Walk-ins need a per-entry Doctor approval (valid for the clinic day, single use).
- **Working-hour extensions:** Reception requests or the Doctor records them; only the Doctor approves; approved extensions extend availability.
- **Doctor leaves early:** Reception/Admin record a disruption; remaining time is blocked; a worklist tracks affected patients with contact logs and outcomes; unresolved appointments are cancelled as `clinic unavailable` (never No-Show).
- **Medical:** visit records, multi-item prescriptions, addenda, attachments (stored outside `wwwroot`), patient history; access limited to the patient and treating doctors.
- **Notifications:** in-app notifications for booking changes, cancellations, disruptions, reminders and staff requests.
- **Analytics:** measured λ, μ, ρ, Wq, Ws, Lq, Ls, utilization, no-show rate and walk-in throughput, with an M/G/1 reference when ρ < 1; Admin clinic dashboard, Doctor statistics, Reception live board.
- **Background maintenance** (every `Clinic:Maintenance:IntervalMinutes`): disruption processing, Grace Period/No-Show, stale queue sweep, previous-day appointment resolution, reminders.

## Configuration

- `ConnectionStrings:MediCare`, `Clinic:TimeZoneId` (deployment value `Africa/Cairo`, no code default), `Clinic:WalkInAdmissionApproval:PendingDurationMinutes`, `Clinic:Maintenance:IntervalMinutes`, `MedicalFiles:StoragePath` (`App_Data/medical-files`, git-ignored, must be outside `wwwroot`), `MedicalFiles:MaxFileSizeBytes`.
- Secrets: `BootstrapAdmin:Email`, `BootstrapAdmin:Password`, `BootstrapAdmin:FullName` via User Secrets or environment variables only.
- Clinic-wide minimum consultation sample size is set by Admin in the database (unset = fallback to configured appointment duration).

## Open / decision required

See "Open / decision required" in `docs/DECISIONS.md`.
