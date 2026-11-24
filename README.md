# MediCare — Clinic Management & Appointment System

Shared foundation for the MediCare clinic system: authentication, authorization, role
management, doctor onboarding/approval and the administration module. The solution is built
with **ASP.NET Core MVC**, **C#**, **EF Core**, **SQL Server**, **ASP.NET Core Identity**,
**Bootstrap 5** and **xUnit** using Clean Architecture.

## Solution structure

```
MediCare.slnx
src/
  MediCare.Domain          Entities, enums, domain invariants (no dependencies)
  MediCare.Application     Use cases, DTOs, validators, interfaces (depends on Domain)
  MediCare.Infrastructure  EF Core, Identity, repositories, email (depends on Application)
  MediCare.Web             MVC controllers, views, policies, middleware
tests/
  MediCare.Tests           xUnit tests for domain, validation and application services
```

Dependency direction: `Web → Infrastructure → Application → Domain`. The Domain project has
no framework dependencies, and business rules are not implemented in controllers or views.

## Roles and authorization

| Role      | Access                                                                 |
|-----------|------------------------------------------------------------------------|
| `Patient` | Patient dashboard, own profile, password change                        |
| `Doctor`  | Doctor area **only after approval**, application status, own profile   |
| `Admin`   | Admin dashboard, doctors, pending applications, users/roles, specializations |

Server-side policies (`MediCare.Application.Common.Security.Policies`):

- `AdminOnly` — requires the `Admin` role.
- `PatientOnly` — requires the `Patient` role.
- `ApprovedDoctor` — requires the `Doctor` role **and** an approved profile. The
  `ApprovedDoctorAuthorizationHandler` re-reads the profile from the database on every
  authorization decision, so revoking approval takes effect immediately.

Additional safeguards:

- Public registration only ever creates `Patient` or `Doctor` accounts. There is no code path
  that lets a visitor self-assign `Admin`.
- Doctor registration creates a `DoctorProfile` with `DoctorApprovalStatus.Pending`. The
  `Doctor` role alone grants no clinical privileges.
- Only administrators can approve, reject, suspend or reactivate doctor profiles.
- `ActiveUserMiddleware` blocks deactivated accounts on the next request, even if they still
  hold a valid authentication cookie.
- The last administrator cannot be demoted or deactivated.
- Administrators cannot change their own role or deactivate their own account.

## Account flows

- Patient registration and doctor registration (pending approval).
- Login/logout with role-aware redirects.
- Email confirmation (configurable) and password recovery.
- Profile management for patients and staff.
- Change password.
- Account activation/deactivation by administrators.

Doctor approval status is deliberately **separate** from the account `IsActive` flag:
approval controls clinical access, while `IsActive` controls whether the account can sign in.

## Administration module

Routes:

- `/Admin/Dashboard`
- `/Admin/Doctors`, `/Admin/Doctors/Pending`, `/Admin/Doctors/Details/{id}`, `/Admin/Doctors/Edit/{id}`
- `/Admin/Users`
- `/Admin/Specializations`

Capabilities: review pending applications, approve/reject/suspend doctors, activate or
deactivate accounts, edit authorized doctor information, change user roles and manage
specializations.

## Configuration

`src/MediCare.Web/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=MediCareDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
  },
  "Identity": {
    "RequireConfirmedEmail": true
  },
  "Seed": {
    "Enabled": true,
    "AdminEmail": "admin@medicare.local",
    "AdminPassword": "Admin@12345",
    "AdminFullName": "MediCare Administrator",
    "SeedSampleSpecializations": true
  },
  "Email": {
    "Host": "",
    "Port": 587,
    "UseSsl": true,
    "FromAddress": "no-reply@medicare.local",
    "FromName": "MediCare",
    "SaveToDisk": true
  }
}
```

- `Identity:RequireConfirmedEmail` — when `true`, new accounts must confirm their email before
  signing in. `appsettings.Development.json` sets it to `false` for a smoother local demo.
- `Seed:Enabled` — applies migrations and seeds roles, the bootstrap administrator and sample
  specializations at startup.
- `Email:Host` — when empty, HTML emails are written to `App_Data/emails` instead of being
  sent. Set an SMTP host, port and credentials to send real mail.

### Seed credentials

| Field    | Value                 |
|----------|-----------------------|
| Email    | `admin@medicare.local` |
| Password | `Admin@12345`         |

Change these values (or override them with user secrets/environment variables) before any
non-demo deployment.

## Database and migrations

The connection string targets SQL Server LocalDB. The application applies pending migrations
during startup when seeding is enabled.

```powershell
# start LocalDB (Windows)
sqllocaldb start MSSQLLocalDB

# create/update the database manually
dotnet tool restore
dotnet ef database update --project src/MediCare.Infrastructure --startup-project src/MediCare.Web

# add a new migration
dotnet ef migrations add <Name> --project src/MediCare.Infrastructure --startup-project src/MediCare.Web
```

The initial migration is
`src/MediCare.Infrastructure/Persistence/Migrations/20261109164912_InitialCreate.cs`.

## Run

```powershell
dotnet run --project src/MediCare.Web
```

The HTTP profile listens on `http://localhost:5132`.

## Tests

```powershell
dotnet test
```

The suite covers domain invariants (doctor approval lifecycle, appointments, availability),
FluentValidation rules and application services (registration, approval, role and account
safeguards). Tests use xUnit and Moq; no database is required.

### End-to-end smoke test

With the app running on `http://localhost:5132`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\smoke-test.ps1
```

The script exercises anonymous access, doctor and patient registration, role separation,
pending-doctor denial, admin approval, immediate doctor access after approval, deactivation
enforcement by `ActiveUserMiddleware`, admin/account page rendering and password recovery. It
creates uniquely-named demo accounts, so it can be re-run without cleaning the database.

## Integration contracts for other modules

Other team modules (appointments, availability, patient records, etc.) should build on these
shared contracts rather than duplicating identity or persistence logic:

- **Entities** (`MediCare.Domain.Entities`): `Appointment`, `DoctorAvailability`,
  `DoctorProfile`, `PatientProfile`, `Specialization`, all deriving from `BaseEntity`.
- **Repository interfaces** (`MediCare.Application.Interfaces.Persistence`):
  `IAppointmentRepository`, `IDoctorRepository`, `IPatientRepository`,
  `ISpecializationRepository`, `IUnitOfWork`.
- **Identity abstraction** (`MediCare.Application.Interfaces.Identity.IIdentityService`) and
  `ICurrentUserService` for the authenticated user.
- **Policies and claims** (`MediCare.Application.Common.Security`): `Roles`, `Policies`,
  `CustomClaimTypes`.
- **DI composition**: `services.AddApplication()` and
  `services.AddInfrastructure(configuration)`.
- **Shared database**: `ApplicationDbContext` owns the Identity schema plus the domain
  entities, so every module uses the same SQL Server database and migration history.

When adding a module, keep controllers thin: validate input, call an application service and
translate the returned `Result` into ModelState errors. Domain invariants belong to entities;
orchestration belongs to application services.
