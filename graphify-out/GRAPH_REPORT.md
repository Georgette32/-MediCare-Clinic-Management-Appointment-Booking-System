# Graph Report - MediCare  (2026-11-04)

## Corpus Check
- Corpus is ~18,825 words - fits in a single context window. You may not need a graph.

## Summary
- 588 nodes · 1409 edges · 60 communities (21 shown, 39 thin omitted)
- Extraction: 89% EXTRACTED · 11% INFERRED · 0% AMBIGUOUS · INFERRED: 158 edges (avg confidence: 0.85)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- Appointment Workflows
- Background Recovery and Tests
- Project Dependencies
- Infrastructure Services and Events
- Waiting List Offers
- Appointment Availability
- Database Schema and Migrations
- Database Context and Scheduling
- Queue Entry Lifecycle
- Doctor Scheduling Settings
- Queue Rules and Estimates
- Appointment Input Models
- Scheduling Rule Tests
- Grace and Duration Rules
- Launch Settings
- Authentication Controllers
- Schedule Settings Form
- Error Handling Views
- Queue Status Lifecycle
- Web Controller Layer
- Layered Architecture
- Registration Validation
- Login Validation
- Queue Workflow Documentation
- Cancellation Recovery Documentation
- Queue Types
- Queue Estimates and Adaptive Duration
- Appointment Database Design
- Check-in and Grace Rules
- Appointment Analytics Events
- Doctor Directory View
- Notification Inbox View
- Doctor Queue View
- Booking Form View
- Login Form View
- Registration Form View
- Waiting List Form View
- Doctor Daily Schedule View
- Patient Appointment History View
- Architecture Guide
- Database Design Guide
- Queue Database Entries
- MVC Route Documentation
- Identity Integration Guide
- Integration Contracts Guide
- Notification Integration Guide
- jQuery License
- Validation Library License
- Unobtrusive Validation License
- ASP.NET MVC Module
- Appointment and Queue Domain
- Smart Queue Guide

## God Nodes (most connected - your core abstractions)
1. `AppointmentService` - 53 edges
2. `IAppointmentService` - 38 edges
3. `Appointment` - 37 edges
4. `QueueEntry` - 35 edges
5. `AppointmentDbContext` - 35 edges
6. `AppointmentsController` - 32 edges
7. `DoctorSchedulingSettings` - 27 edges
8. `WaitingListEntry` - 25 edges
9. `AppointmentOffer` - 24 edges
10. `AppointmentEvent` - 24 edges

## Surprising Connections (you probably didn't know these)
- `Cancellation Recovery Offers` --semantically_similar_to--> `Cancellation Recovery Through Waiting List`  [INFERRED] [semantically similar]
  docs/BusinessRules.md → README.md
- `QueueEstimate` --references--> `QueueType`  [EXTRACTED]
  MediCare.Application/Scheduling.cs → MediCare.Domain/Entities.cs
- `QueueSnapshot` --references--> `QueueEntry`  [EXTRACTED]
  MediCare.Application/Scheduling.cs → MediCare.Domain/Entities.cs
- `AppointmentService` --implements--> `IAppointmentService`  [EXTRACTED]
  MediCare.Infrastructure/Services/AppointmentService.cs → MediCare.Application/Scheduling.cs
- `AppointmentDbContext` --references--> `DoctorSchedulingSettings`  [EXTRACTED]
  MediCare.Infrastructure/Persistence/AppointmentDbContext.cs → MediCare.Domain/Entities.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **MediCare Layer Dependency Direction** — docs_architecture_domain_layer, docs_architecture_application_layer, docs_architecture_infrastructure_layer, docs_architecture_web_layer [EXTRACTED 1.00]

## Communities (60 total, 39 thin omitted)

### Community 0 - "Appointment Workflows"
Cohesion: 0.09
Nodes (4): DoctorDirectoryItem, IAppointmentService, AppointmentsController, UserKey

### Community 1 - "Background Recovery and Tests"
Cohesion: 0.09
Nodes (15): MediCare.Infrastructure.Services, MediCare.Tests.Integration, MediCare.Web.Services, MediCare.Infrastructure, MediCare.Application, MediCare.Tests, MediCare.Domain, MediCare.Infrastructure.Persistence.Migrations (+7 more)

### Community 2 - "Project Dependencies"
Cohesion: 0.05
Nodes (47): MediCare.Application, net10.0, Microsoft.AspNetCore.Identity.EntityFrameworkCore (10.0.14), Microsoft.EntityFrameworkCore.Design (10.0.14), Microsoft.EntityFrameworkCore.SqlServer (10.0.14), Microsoft.EntityFrameworkCore.Tools (10.0.14), Microsoft.NET.Test.Sdk (17.14.1), System.Security.Cryptography.Pkcs (10.0.14) (+39 more)

### Community 3 - "Infrastructure Services and Events"
Cohesion: 0.14
Nodes (13): IAppointmentNotifier, AppointmentEvent, AppointmentId, Details, DoctorId, EventType, Id, OccurredAt (+5 more)

### Community 4 - "Waiting List Offers"
Cohesion: 0.05
Nodes (42): AppointmentOffer, CreatedAt, DoctorId, ExpiresAt, Id, Outcome, PatientId, RespondedAt (+34 more)

### Community 5 - "Appointment Availability"
Cohesion: 0.07
Nodes (31): Appointment, ArrivedAt, CancellationReason, CancelledAt, CheckedInAt, ConfirmedAt, ConsultationEndedAt, ConsultationStartedAt (+23 more)

### Community 6 - "Database Schema and Migrations"
Cohesion: 0.09
Nodes (15): ScheduleEvent, AppointmentNotification, CreatedAt, Id, Message, OfferId, PatientId, ReadAt (+7 more)

### Community 7 - "Database Context and Scheduling"
Cohesion: 0.06
Nodes (28): DoctorBreak, DoctorId, EndsAt, Id, IsRecurring, RecurringDay, RecurringEnd, RecurringStart (+20 more)

### Community 8 - "Queue Entry Lifecycle"
Cohesion: 0.11
Nodes (18): QueueEntry, Appointment, AppointmentId, Approval, ApprovalTime, ApprovedByDoctorId, ArrivalTime, CalledAt (+10 more)

### Community 9 - "Doctor Scheduling Settings"
Cohesion: 0.15
Nodes (13): DoctorSchedulingSettings, BookedCapacityReservation, CallTimeoutMinutes, ConsultationFee, DefaultConsultationDurationMinutes, DoctorId, GracePeriodMultiplier, MinimumHistorySamples (+5 more)

### Community 10 - "Queue Rules and Estimates"
Cohesion: 0.24
Nodes (4): QueueEstimate, QueueEstimateCalculator, QueuePolicy, QueueSnapshot

### Community 11 - "Appointment Input Models"
Cohesion: 0.17
Nodes (9): BookAppointmentInput, DoctorId, Start, WaitingListInput, DateFrom, DateTo, DoctorId, TimeFrom (+1 more)

### Community 14 - "Launch Settings"
Cohesion: 0.20
Nodes (9): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, profiles, http (+1 more)

### Community 16 - "Schedule Settings Form"
Cohesion: 0.22
Nodes (9): ScheduleSettingsInput, BookedCapacityReservation, ConsultationFee, DurationMinutes, GraceMultiplier, MinimumSamples, PublicDisplayName, SafetyBufferMultiplier (+1 more)

### Community 17 - "Error Handling Views"
Cohesion: 0.25
Nodes (4): MediCare.Web.Models, ErrorViewModel, RequestId, ShowRequestId

### Community 18 - "Queue Status Lifecycle"
Cohesion: 0.25
Nodes (8): QueueStatus, Called, Completed, Deferred, InConsultation, Rejected, TimedOut, Waiting

### Community 20 - "Layered Architecture"
Cohesion: 0.40
Nodes (6): Application Layer, AppointmentService, Domain Layer, Infrastructure Layer, No-op Notification Adapter, Web Layer

### Community 21 - "Registration Validation"
Cohesion: 0.40
Nodes (4): RegisterInput, ConfirmPassword, Email, Password

### Community 22 - "Login Validation"
Cohesion: 0.40
Nodes (5): LoginInput, Email, Password, RememberMe, ReturnUrl

### Community 24 - "Cancellation Recovery Documentation"
Cohesion: 0.67
Nodes (3): Cancellation Recovery Offers, Waiting List and Appointment Offers, Cancellation Recovery Through Waiting List

### Community 25 - "Queue Types"
Cohesion: 0.67
Nodes (3): QueueType, Booked, WalkIn

## Knowledge Gaps
- **246 isolated node(s):** `net10.0`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore (10.0.14)`, `Microsoft.EntityFrameworkCore.Design (10.0.14)`, `Microsoft.EntityFrameworkCore.SqlServer (10.0.14)`, `Microsoft.EntityFrameworkCore.Tools (10.0.14)` (+241 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 300 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **39 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `AppointmentDbContext` connect `Database Context and Scheduling` to `Background Recovery and Tests`, `Infrastructure Services and Events`, `Waiting List Offers`, `Appointment Availability`, `Database Schema and Migrations`, `Queue Entry Lifecycle`, `Doctor Scheduling Settings`, `Authentication Controllers`?**
  _High betweenness centrality (0.093) - this node is a cross-community bridge._
- **Are the 10 inferred relationships involving `AppointmentService` (e.g. with `SqlServerAppointmentTests.Booking_is_persisted_and_overlapping_booking_is_rejected()` and `SqlServerAppointmentTests.Booking_rejects_outside_hours_breaks_and_approved_leave()`) actually correct?**
  _`AppointmentService` has 10 INFERRED edges - model-reasoned connections that need verification._
- **What connects `net10.0`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore (10.0.14)`, `Microsoft.EntityFrameworkCore.Design (10.0.14)` to the rest of the system?**
  _246 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Appointment Workflows` be split into smaller, more focused modules?**
  _Cohesion score 0.09327731092436975 - nodes in this community are weakly interconnected._
- **Why does `QueueEntry` connect `Queue Entry Lifecycle` to `Appointment Workflows`, `Infrastructure Services and Events`, `Waiting List Offers`, `Appointment Availability`, `Database Schema and Migrations`, `Database Context and Scheduling`, `Queue Rules and Estimates`, `Scheduling Rule Tests`, `Grace and Duration Rules`, `Queue Status Lifecycle`, `Queue Types`?**
  _High betweenness centrality (0.074) - this node is a cross-community bridge._
- **Should `Background Recovery and Tests` be split into smaller, more focused modules?**
  _Cohesion score 0.08734693877551021 - nodes in this community are weakly interconnected._
- **Why does `Appointment` connect `Appointment Availability` to `Appointment Workflows`, `Infrastructure Services and Events`, `Waiting List Offers`, `Database Schema and Migrations`, `Database Context and Scheduling`, `Queue Entry Lifecycle`, `Queue Rules and Estimates`, `Scheduling Rule Tests`?**
  _High betweenness centrality (0.061) - this node is a cross-community bridge._