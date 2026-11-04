# Appointments and Smart Queue

## Doctor queue sessions

`DoctorQueueSession` is a persistent, doctor-owned lifecycle record. It stores `DoctorId`, `SessionDate`, `Status`, `CreatedAt`, and nullable `StartedAt` and `ClosedAt` UTC timestamps. Its state is one of:

- `NotStarted`: prepared explicitly by the doctor; queue work has not started.
- `Active`: started explicitly by the doctor and eligible for queue operations.
- `Closed`: ended explicitly by the doctor. It is terminal and cannot be reopened.

The only allowed transitions are `NotStarted → Active` and `Active → Closed`. Closing is rejected while a consultation is `InConsultation`. Completing a consultation does not close its session. A doctor can create multiple sessions on one calendar date; the database permits at most one Active session per doctor at a time.

The queue page does not create sessions on read. The doctor explicitly prepares a NotStarted session, starts it, then closes it when finished. There is no automatic closure at working-hours end, appointment completion, calendar date change, or background-worker execution. A closed session does not cancel appointments, delete queue entries, or create a replacement session.

## Authorization and queue ownership

The authenticated ASP.NET Core Identity user ID is the doctor key. MVC actions pass that claim-derived ID to the application service rather than accepting a DoctorId from form data. Session lookups and transitions are scoped by both session ID and doctor ID. Existing Doctor/Admin role authorization remains in place; Admin actions remain self-scoped and no cross-doctor privilege was added.

Appointments and QueueEntry rows remain doctor-owned by their existing external `DoctorId` string keys. A QueueEntry records the Active session associated with check-in, approval, or call through nullable `DoctorQueueSessionId`; check-ins before any session exists remain unassociated. A later call associates a waiting entry with that Active session. StartConsultation requires the exact same Active session that made the call, so a call left over from a Closed session cannot be used in a later session. Waiting entries and appointments remain stored across manual closure and can be called in a later session. Check-in does not create or activate a session.

Walk-in registration/approval, CallNext, StartConsultation, and CompleteConsultation require an Active session. CallNext also preserves the existing booked priority, approved Walk-in FIFO, one-call limit, five-minute timeout processing, and prohibition on calling during an active consultation. StartConsultation validates the Active session and a still-valid Called entry. Session closure rejects an active consultation. Waiting appointments and queue entries are retained on closure.

## Persistence and concurrency

Session transitions and queue operations use SQL Server Serializable transactions. A row-version token protects concurrent edits to an individual session. A filtered unique index on `(DoctorId, Status)` for `Status = Active` prevents two Active sessions for one doctor at the database level. Existing QueueEntry row-version handling and call timeout behavior remain in place.

`AppointmentRecoveryWorker` only expires appointment offers and timed-out calls. It has no session lifecycle operation and never starts, closes, or reopens a session.

Apply the `DoctorQueueSessionLifecycle` migration with:

```powershell
dotnet ef database update --project MediCare.Infrastructure --startup-project MediCare.Web
```
