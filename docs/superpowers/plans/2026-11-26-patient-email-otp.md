# Patient Email OTP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** New self-registered patients confirm their email with a 6-digit code before they can use MediCare; existing accounts are untouched.

**Architecture:** Five new columns on `AspNetUsers` (flag + code state, all defaulting to "not required"). An Infrastructure `EmailVerificationService` owns code generation, hashing, expiry, cooldown and attempts on top of `UserManager`; an `IEmailSender` sends the branded email (Gmail SMTP, or the log in Development). The existing session middleware redirects pending users to `/Account/VerifyEmail`; the page talks to three JSON endpoints and plays the approved animations.

**Tech Stack:** ASP.NET Core 10 MVC, Identity, EF Core 10 (SQL Server), System.Net.Mail, xUnit + WebApplicationFactory.

**Spec:** `docs/superpowers/specs/2026-11-26-patient-email-otp-design.md` (prototype: `docs/superpowers/specs/email-otp-mockup/`)

## Global Constraints
- Never change or delete existing rows. New columns: `RequiresEmailVerification bit NOT NULL DEFAULT 0`, `EmailCodeFailedAttempts int NOT NULL DEFAULT 0`, the rest nullable.
- Code: 6 digits from `RandomNumberGenerator`, stored only as a hash, valid 10 minutes, single use, 60 s resend cooldown, 5 wrong tries kill the code.
- Only `RegisterPatientAsync` sets the flag. Reception/admin-created accounts and seeders never do.
- Sender: `medicare58.runasp.net@gmail.com`, `smtp.gmail.com:587` STARTTLS. Password only in `appsettings.Production.json`/user secrets. Empty password → code goes to the log.
- Success neon = identity blue `--teal`. Wrong = vermilion + shake. `prefers-reduced-motion` honoured.
- No Claude attribution in commits.

## Review Focus
- Reload of the verify page must not send a new email each time (cooldown) yet must send one when the code has expired.
- Changing the email to one already used by another account is refused without revealing whose it is.
- A pending user who signs out and signs in later lands back on the verify page, never inside the app.
- Demo/existing patients (flag false, EmailConfirmed false) sign in exactly as before.
- SMTP failure during sign-up does not lose the account: the page loads and offers Resend.

---

### Task 1: Schema
**Files:** Modify `MediCare.Infrastructure/Identity/ApplicationUser.cs`, `Persistence/MediCareDbContext.cs`; Create migration `AddPatientEmailVerification`; Create `deploy/add-patient-email-verification.sql` (idempotent script for this migration only).
- [ ] Add `RequiresEmailVerification`, `EmailCodeHash` (max 64), `EmailCodeExpiresAtUtc`, `EmailCodeSentAtUtc`, `EmailCodeFailedAttempts`.
- [ ] `dotnet ef migrations add` then `dotnet ef migrations script ClientGeneratedKeys AddPatientEmailVerification --idempotent`.
- [ ] Apply to the local database with `dotnet ef database update` (adds columns only).

### Task 2: Email sending
**Files:** Create `MediCare.Infrastructure/Email/{EmailOptions,IEmailSender,SmtpEmailSender,LogEmailSender,VerificationEmail}.cs`, `Email/Assets/{mark.png,clock.png}` (embedded).
**Produces:** `IEmailSender.SendAsync(EmailMessage, CancellationToken)`; `VerificationEmail.Build(string firstName, string code, DateTimeOffset expiresAtClinic, string toAddress, string siteUrl) : EmailMessage` (subject, html, text, inline images by Content-ID).
- [ ] Test: html contains each digit, the expiry time "HH:mm", the address, `cid:mc-mark`; text part contains the code; subject contains the code.

### Task 3: Verification service
**Files:** Create `MediCare.Infrastructure/Identity/EmailVerificationService.cs`; Test `MediCare.Tests/EmailVerificationServiceTests.cs`.
**Produces:** `IEmailVerificationService` with
`SendCodeAsync(ApplicationUser, string siteUrl, CancellationToken) : Task<EmailCodeSend>` (Sent | CoolingDown(seconds) | Failed),
`VerifyAsync(ApplicationUser, string code) : Task<EmailCodeCheck>` (Confirmed | Wrong(triesLeft) | Expired | Locked),
`ChangeEmailAsync(ApplicationUser, string email, string siteUrl, CancellationToken) : Task<EmailChange>`,
`static bool IsPending(ApplicationUser)`, `static string Mask(string email)`.
- [ ] Tests (fake clock + capturing sender): send stores hash not code; second send within 60 s → CoolingDown; wrong code decrements to 4; 5th wrong → Locked and correct code then refused; expired after 10 min; correct → EmailConfirmed true and code fields cleared; change email to taken address refused.

### Task 4: Registration, endpoints, guard
**Files:** Modify `IIdentityAccountStore.CreateAsync` (+`bool requireEmailVerification`), `IdentityAccountStore`, `AccountProvisioningService.RegisterPatientAsync` (email required, flag on), three test fakes; `AccountController` (Register signs in + sends + redirects; `VerifyEmail` GET/POST, `ResendCode`, `ChangeEmail`); `PatientRegistrationViewModel.Email` `[Required]`; `MustChangePasswordMiddleware` + new `Filters/EmailVerificationPolicy.cs`; rate-limit policy `Otp` (appsettings 30 per 600 s).
- [ ] Tests: policy allows verify/resend/change/logout/static only; registering without email fails; integration: register → lands on `/Account/VerifyEmail`, `/` redirects there, wrong code JSON `wrong`, right code JSON `ok` then `/Appointments/Mine` is 200; demo patient sign-in unaffected.

### Task 5: Page, motion, layout
**Files:** Create `Views/Account/VerifyEmail.cshtml`, `wwwroot/js/verify-email.js`; Modify `wwwroot/css/medicare.css` (Email verification section), `Views/Shared/_Layout.cshtml` (`ViewData["PublicChrome"]` shows the public header with a Sign out button), `Views/Account/Register.cshtml` (email required copy).
- [ ] Port the approved prototype; JS posts with the antiforgery token and `X-Requested-With: fetch`; server is the source of truth for expiry and tries.

### Task 6: Verify end to end
- [ ] `dotnet test` all green; run the app, register, read the code from the log, walk wrong → right; screenshot desktop/mobile/dark; stop LocalDB afterwards.
