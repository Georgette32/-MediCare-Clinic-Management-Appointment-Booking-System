# Database design

EF Core migration `InitialCreate` contains ASP.NET Core Identity tables and the module tables.

- `Appointments` contains patient/doctor external IDs, UTC scheduled and operational timestamps, lifecycle status, cancellation/no-show audit fields, and a SQL Server row version. An index supports doctor/time conflict queries and one supports patient history.
- `DoctorSchedulingSettings` is keyed by external `DoctorId`; working hours have a unique doctor/day constraint. Break and leave rows have date bounds and recurrence/approval flags.
- `QueueEntries` link optional appointments and record queue type/state, arrival/check-in, approval, call expiry, consultation entry, and completion. Nullable `DoctorQueueSessionId` links a queue entry to its active session when check-in/approval/call occurs; pre-session arrivals remain unassociated. The filtered unique index prevents duplicate booked queue entries per appointment. Row version is enabled.
- `DoctorQueueSessions` stores the doctor's session date, `NotStarted`/`Active`/`Closed` status, UTC lifecycle timestamps, and row version. Multiple sessions per doctor and date are allowed. A SQL Server filtered unique index on `(DoctorId, Status)` where `Status = 1` ensures only one Active session per doctor. Doctor IDs remain external Identity string keys, consistent with the absence of a Doctor aggregate in this module.
- `WaitingListEntries` hold patient preferences. `AppointmentOffers` retain matched slot, slot-release timestamp, and offer lifecycle with row version and indexes. `AppointmentNotifications` persists offer messages in the local patient inbox and has a unique reference to its offer.
- `AppointmentEvents` records append-only operational events with UTC timestamps and references by identifiers.

Patient/doctor IDs deliberately have no foreign keys until the owning team entity contracts are available. Status values and event names are stored in EF's enum/string mapping defaults; consumers should use the domain enums and event names. SQL Server identity tables share this database context.

Queue sessions are historical records and are never automatically closed or reopened. Session state changes use Serializable transactions and row-version checks; the filtered index is the final database-level guard against concurrent Active sessions.
