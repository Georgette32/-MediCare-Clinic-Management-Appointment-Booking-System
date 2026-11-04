# Queue flow

1. Patient checks in. An appointment arrived by the calculated grace boundary enters as booked; a late arrival retains the appointment but enters the queue as a pending walk-in candidate.
2. Doctor explicitly approves/rejects/defer walk-ins. Only approved walk-ins enter the active FIFO.
3. The doctor explicitly prepares a queue session, then explicitly starts it. No call or consultation can start until an owned session is Active. Sessions move NotStarted → Active → Closed and are retained as history. A doctor can have multiple sessions per calendar date, but only one Active session at a time.
4. While the session is Active, the doctor calls an eligible patient. Only one call is active; its five-minute timeout is processed before another call can be selected. Calls remain separate from session lifecycle.
5. The doctor completes the active consultation explicitly. A session cannot be closed while a consultation is in progress. The called patient can then start; a current consultation is never preempted.
6. The doctor must explicitly close an Active session. Closure never happens at the end of working hours, after appointments finish, on a date change, or from a background worker. Closing leaves appointments, queue entries, and history stored.
7. Each subsequent choice prioritizes eligible checked-in bookings by scheduled start and check-in, then approved walk-ins by arrival. Queue state is queried live rather than snapshotted at the beginning of the day.

If an earlier booked patient arrives during a walk-in consultation, they remain eligible and will outrank later bookings/walk-ins at the next handoff. The room count includes waiting and called patients, not the patient in consultation. When seats are full, check-in fails clearly; an approved walk-in must defer or be rejected.

Queue sessions are doctor-owned through the authenticated Identity user ID. MVC routes always pass the signed-in user's ID; submitted session IDs are resolved together with that doctor ID, so another doctor's session cannot be operated by changing a form value. Arrival/check-in can still create queue records before a session starts; they do not create or activate a session. Walk-in registration and approval, calling, starting, and completing consultations require the doctor's Active session.

When an entry is checked in during an Active session, approved, or called, `QueueEntry.DoctorQueueSessionId` records that session. A called patient can start consultation only in the same still-Active session. If the session is manually closed while a call is outstanding, that call cannot be started in a later session; normal five-minute timeout recovery remains responsible for expiring it. Waiting entries remain stored and may be called in a later session.

## Estimates

Consultation durations use the most recent 30 valid completed encounters. Estimates use remaining expected time for the active consultation, expected durations ahead, and booked appointment times. The doctor queue page refreshes every 30 seconds. They remain estimates, not punctuality guarantees.
