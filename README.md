# MediCare Appointments and Smart Queue

ASP.NET Core MVC module for doctor schedules, appointment lifecycle, check-in, smart queue and walk-in handling, and cancellation recovery through a waiting list. The solution targets .NET 10.

## Requirements and setup

- .NET SDK 10.0.2xx or later.
- SQL Server (LocalDB is the development default).
- Configure `ConnectionStrings:MediCare` in user secrets or environment configuration for non-local deployments. Do not commit credentials.
- Run `dotnet restore MediCare.slnx`, `dotnet ef database update --project MediCare.Infrastructure --startup-project MediCare.Web`, then `dotnet run --project MediCare.Web`.
- Run `dotnet test MediCare.Tests` for the unit and SQL Server LocalDB integration tests.

The empty starter repository did not contain Identity users, patient/doctor records, or team integration services. ASP.NET Core Identity is included in the database. Patient and doctor keys are Identity user ID values and can be replaced by adapters to the team's established contracts. Assign the Doctor role to an approved account, then use Schedule Settings to create the doctor's settings and hours before booking. Offer notifications are persisted to the local appointment notification inbox; connect this adapter to Georgette's Notifications module before integrating the team databases.

## Configuration defaults

Doctor scheduling settings are per doctor. Provisional defaults: 20 minute consultation, 2x grace multiplier, 5 minute call timeout, 3 waiting seats, 0.5 adaptive safety-buffer multiplier, 5 historical consultations minimum, 1 reserved booked seat, and a 15 minute offer lifetime. Most settings are available in the doctor schedule page. The waiting-list matching policy selects the oldest matching active registration. The call timeout and room capacity are fixed at 5 minutes and 3 seats.

All persisted timestamps are UTC. MVC time inputs are labeled UTC; clinic-local time conversion remains dependent on a team time-zone contract.

## Scope notes

The migrations create Identity, appointment, scheduling, queue, offer, event, and notification tables. SQL Server serializable transactions protect booking, seat claims, queue state changes, and offer claims; row-version tokens are used for mutable entities. Integration tests create and delete uniquely named LocalDB databases to verify persisted booking and competing offer acceptance.
