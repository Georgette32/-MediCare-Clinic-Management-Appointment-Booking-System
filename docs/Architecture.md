# Architecture

`MediCare.Domain` owns appointment, scheduling, queue, waiting-list, and event entities and enums. `MediCare.Application` owns service contracts, queue snapshots, duration statistics, and deterministic scheduling rules. `MediCare.Infrastructure` implements the application contract with EF Core and SQL Server, including Identity persistence and transaction boundaries. `MediCare.Web` composes the application, authenticates users, authorizes patient/doctor/admin actions, and renders MVC screens. `MediCare.Tests` covers pure scheduling rules.

Dependency direction is Domain <- Application <- Infrastructure; Web references Application and Infrastructure for composition. Patient and doctor identifiers are external string keys; no duplicate patient or doctor aggregates are created. Controllers do not use DbContext directly.

`AppointmentService` currently groups persistence use cases behind one application contract. As team modules become available, split the interface by feature and replace the no-op notification adapter with the Notifications contract.
