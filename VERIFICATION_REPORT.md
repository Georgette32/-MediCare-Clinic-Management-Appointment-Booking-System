# MediCare Verification Report

**Audit date:** 2026-11-23<br>
**Scope:** Read-only verification of repository state. No source or configuration files were edited. The only authored file is this report. dotnet build, dotnet test, and EF migration inspection were run as permitted; the EF history inspection issued SELECT queries only.

## A. Environment and framework

| Item | Status | Evidence |
|---|---|---|
| SDK pin | Verified | global.json:2-6 pins SDK 10.0.401; dotnet --info reported SDK 10.0.401 (also 10.0.202 installed). |
| Project target frameworks | Verified | All five projects target net10.0: MediCare.Application/MediCare.Application.csproj:9, MediCare.Domain/MediCare.Domain.csproj, MediCare.Infrastructure/MediCare.Infrastructure.csproj:16, MediCare.Tests/MediCare.Tests.csproj, MediCare.Web/MediCare.Web.csproj:13. |
| EF Core / Identity package versions | Verified | MediCare.Infrastructure/MediCare.Infrastructure.csproj:7-12 uses EF/Identity 10.0.0; Web Design package and Application Options package are also 10.0.0. |
| Microsoft.Extensions.Options | Verified | MediCare.Application/MediCare.Application.csproj:4 contains package 10.0.0. |
| Stray >> in Infrastructure project | Verified | No >> appears in MediCare.Infrastructure/MediCare.Infrastructure.csproj; project XML built through Infrastructure successfully. |
| Leftover net11 / 11.0 references | Mismatch | docs/TECHNICAL_SPECIFICATION.md:27-29 still marks SDK 11.0.100, net11.0, and EF 11.0.0 as implemented. docs/WORK_LOG.md:6,8 has the old SDK claims. artifacts/InitialCreate.sql:236 records ProductVersion 11.0.0. Source projects and migration snapshot/designer metadata are net10 / ProductVersion 10.0.0. dotnet ef --version reports CLI 11.0.0, despite project packages being 10.0.0. No net11.0 target appears in project files or global.json. |

## B. Phase 1 / 2A

| Item | Status | Evidence |
|---|---|---|
| Four roles | Verified | MediCare.Infrastructure/Identity/MediCareRoles.cs:3-9: Admin, Doctor, Receptionist, Patient. |
| ApplicationUser | Verified | MediCare.Infrastructure/Identity/ApplicationUser.cs:5-10. |
| Unit of work | Verified | MediCare.Application/Abstractions/IUnitOfWork.cs:5-14; EF implementation at MediCare.Infrastructure/Persistence/EfUnitOfWork.cs:8-22. |
| Appointment repository | Verified | Interface at MediCare.Application/Abstractions/IAppointmentRepository.cs:5-18; EF implementation at MediCare.Infrastructure/Persistence/EfAppointmentRepository.cs:8-64. |
| Filtered unique doctor/date/start index | Verified | MediCare.Infrastructure/Persistence/MediCareDbContext.cs:83-88; unique index filters Pending and Confirmed. |
| Appointment/break/work-hour/holiday range checks | Verified | MediCare.Infrastructure/Persistence/MediCareDbContext.cs:58,66,75,92-96. |
| Clinic clock, Cairo and UTC handling | Partial | MediCare.Application/Abstractions/IClinicClock.cs:3-7 and MediCare.Infrastructure/Time/SystemClinicClock.cs resolve configured local time and UTC. MediCare.Infrastructure/DependencyInjection.cs:20-22 requires Clinic:TimeZoneId and does not default it. Inspected tracked configuration did not establish that deployment value is Africa/Cairo; timestamps are normalized with ToUniversalTime() in appointment/queue operations. |
| Double-booking protection | Verified | Filtered unique index above plus overlap query EfAppointmentRepository.cs:28-35; AppointmentService.cs:15,27,40,49,77,94,126 uses serializable transactions for booking and mutations. |
| Booking, confirm/reject, cancel, reschedule workflows | Verified | MediCare.Application/Services/AppointmentService.cs:13-110 (patient/clinic cancellation, patient/clinic rescheduling, confirm/reject); transactional helpers at 124 onward. |

## C. Phase 2B verification

| Item | Status | Evidence |
|---|---|---|
| Frozen grace-period fields | Verified | MediCare.Domain/Entities/Appointment.cs:20-21. |
| Start, expiry and No-Show domain methods | Partial | Methods at Appointment.cs:107-136; start requires Confirmed, positive minutes, and one-time freeze; expiry is inclusive; No-Show requires Confirmed. Service does not check that appointment start time has passed before starting the timer. |
| Queue source, sequence and state transitions | Verified | MediCare.Domain/Entities/QueueEntry.cs:9-45,66-120; scheduled entries omit sequence, walk-ins require positive sequence; transitions enforce Waiting→Called→InConsultation→Completed and allow Cancelled/NoShow from guarded states. |
| Grace-period DB check constraint | Verified | MediCare.Infrastructure/Persistence/MediCareDbContext.cs:92-96. |
| Confirmed appointments query | Verified | Interface MediCare.Application/Abstractions/IAppointmentRepository.cs:17; EF method MediCare.Infrastructure/Persistence/EfAppointmentRepository.cs:57-64. |
| Completed visit query | Verified | Interface MediCare.Application/Abstractions/IQueueRepository.cs; EF method MediCare.Infrastructure/Persistence/EfQueueRepository.cs:62-71 filters Completed visits with both timestamps. |
| DI registration | Verified | MediCare.Infrastructure/DependencyInjection.cs:35-36. |
| Expected-duration options and calculation | Partial | MediCare.Application/Common/ExpectedDurationOptions.cs:3-10 has a 60-minute cap, 30-day window, multiplier 3, and nullable/no-default minimum sample size. ExpectedDurationService.cs:14-50 uses completed timestamps and fallback to configured doctor duration. It filters duration 0 < minutes < cap; configuration is not validated for nonsensical cap/sample values. |
| Grace-period service guards/idempotency | Partial | MediCare.Application/Services/GracePeriodService.cs:16-55 guards non-confirmed, wrong clinic date, and already checked-in appointments and returns the frozen value idempotently. It can start before the appointment's scheduled time. Expiry processing at :58-87 skips checked-in appointments and marks expired confirmed appointments No-Show; it accepts a caller-supplied clinic date without checking it equals IClinicClock.Today. |
| Work-log Phase 2B.1 entry | Verified | docs/WORK_LOG.md:78-83 records queue repository and check-in work and claimed test/build results. |
| Decisions Phase 2B.1 entry | Partial | docs/DECISIONS.md:76-80 states Step 2B.1 implements queue/check-in, but the same section says Grace Period and expected-duration/no-show processing remain future work, stale relative to present code. |

## D. Migrations and database

| Item | Status | Evidence |
|---|---|---|
| InitialCreate and InitialCreate_net10 order | Verified | dotnet ef migrations list --project MediCare.Infrastructure/MediCare.Infrastructure.csproj --startup-project MediCare.Web/MediCare.Web.csproj listed 20261121190210_InitialCreate then 20261123000418_InitialCreate_net10. The read-only command queried __EFMigrationsHistory; both appeared applied (no Pending marker). |
| EF snapshot version / pending model changes | Verified | MediCare.Infrastructure/Persistence/Migrations/MediCareDbContextModelSnapshot.cs:20 has ProductVersion 10.0.0. dotnet ef migrations has-pending-model-changes returned “No changes have been made to the model since the last migration.” |
| net10 migration operations | Verified | MediCare.Infrastructure/Persistence/Migrations/20261123000418_InitialCreate_net10.cs:14-29 adds both grace fields and positive constraint; Up contains no table creation. |
| Local DB migration history | Verified | EF migration listing successfully read __EFMigrationsHistory and listed both migrations as applied; SELECT only. |

## E. Tests

| Item | Status | Evidence |
|---|---|---|
| Check-in fake repository methods | Verified | MediCare.Tests/CheckInServiceTests.cs:311-312,363-364 implements confirmed-appointment and completed-visit repository methods. |
| ExpectedDurationServiceTests count | Verified | 8 [Fact] methods in MediCare.Tests/ExpectedDurationServiceTests.cs (lines 51-145). |
| GracePeriodServiceTests count | Mismatch | 11 [Fact] methods in MediCare.Tests/GracePeriodServiceTests.cs (lines 44-192), not 9 as claimed. |
| Full test execution | Missing | dotnet test MediCare.slnx did not execute tests: test-project compilation failed. Result: 0 passed, 0 failed, 0 skipped; no test cases ran. |

**Build:** dotnet build MediCare.slnx failed. It reported 12 NuGet vulnerability warning instances (NU1902 moderate and NU1903 high for System.Security.Cryptography.Pkcs 9.0.4 across Infrastructure, Web, and Tests) and 3 compiler errors, all in MediCare.Tests/GracePeriodServiceTests.cs: line 55 references nonexistent GracePeriodStartResult.GracePeriodMinutes; line 56 references nonexistent .GracePeriodStartedAtUtc; line 76 again references nonexistent .GracePeriodMinutes. The result record in MediCare.Application/Services/IGracePeriodService.cs:9-13 exposes different property names. Initial sandboxed build failed before compilation due to SDK workload initialization access denial; the completed elevated retry produced these results.

**Test totals:** Current test project declares 79 [Fact] attributes by source scan (including integration-test facts); runtime totals cannot be reported because compilation failed. This differs from the stale “61 automated tests” claim in docs/TECHNICAL_SPECIFICATION.md:32.

## F. Specification consistency and discrepancies

1. **Critical — tests/build are broken:** 3 stale result-property references prevent the test assembly from compiling, so no automated tests ran.
2. **High — grace period can begin early:** GracePeriodService.StartGracePeriodIfEligibleAsync checks appointment date but not whether current clinic time has reached Appointment.StartTime; it can freeze/consume the grace window before the scheduled appointment time. Expiry processing also accepts a non-current clinic date.
3. **High — framework/testing docs are stale:** technical spec still claims .NET/EF 11 and 61 tests; work log still claims SDK 11. The SQL review artifact retains ProductVersion 11.0.0. Current projects and snapshot are net10 / EF 10, while installed dotnet-ef CLI is 11.0.0.
4. **Medium — progress checklist unavailable:** docs/PROGRESS.md is referenced by docs/BUSINESS_IDEA.md:193 but does not exist in the workspace. Its [✓] items cannot be audited. Technical spec itself marks multiple items implemented; runtime/EF versions and test count are contradicted above. Phase 2B grace/expected-duration code exists but is not reflected as completed in the Phase 2B boundary in docs/DECISIONS.md:76-80.
5. **Medium — grace-policy wording:** business requirements/spec call for configurable ClinicSettings.LateArrivalGracePeriodMinutes; no code references ClinicSettings or LateArrivalGracePeriodMinutes. docs/DECISIONS.md:21,23 records the alternative policy (expected duration with fallback and 3× multiplier, frozen per appointment), so the decision is documented, but it does not explicitly label itself as a deviation from that spec field. ExpectedDurationOptions.MinimumSampleSize remains nullable with no hardcoded default, as the decision requires.
6. **Low — deployment timezone evidence:** implementation requires a timezone configuration value but this audit did not verify that the active deployment configuration sets it to Africa/Cairo.

**Database change required?** No current schema change is indicated: EF reports no pending model changes, both migrations are listed as applied in local migration history, and InitialCreate_net10 contains the required grace columns and constraint. This conclusion covers schema only; it does not resolve the build/test failure or behavioral findings above.
