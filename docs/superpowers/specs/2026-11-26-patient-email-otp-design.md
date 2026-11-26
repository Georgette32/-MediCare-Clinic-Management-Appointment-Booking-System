# Patient email verification (OTP) — design

Date: 2026-11-26 · Status: awaiting approval (revised with motion and identity-blue neon)

## Intent
Make sure every **new** patient account belongs to a real person with a working email. At sign-up the patient gets a
6-digit code by email, in MediCare's visual identity, and the account cannot be used until the code is entered.

## Goals
- Email becomes **required** on the public patient sign-up form.
- A branded HTML email (navy header, MediCare mark, blue/cyan accents, code in large spaced digits, plain-text fallback).
- A "Verify your email" page in the same style as Sign in / Create account: 6 separate digit boxes with paste support,
  a resend button with a 60-second countdown, and "Wrong email? Change it".
- Unverified accounts can sign in only as far as the verify page; everything else redirects there.

## Non-goals / anti-goals
- **Do not delete or change any existing data.** Existing patients, staff and demo accounts are never asked to verify.
- No verification for accounts created by reception or admin (staff hand those over in person).
- No verification on profile email changes in this round (can follow later).
- Failure if: an existing/demo account gets locked out, or a sign-up silently fails because email sending broke.

## Approaches considered
1. **Flag + own code fields on the account (chosen).** New nullable columns on `AspNetUsers`:
   `RequiresEmailVerification` (default `false`, so every existing row stays unaffected), `EmailCodeHash`,
   `EmailCodeExpiresAtUtc`, `EmailCodeSentAtUtc`, `EmailCodeFailedAttempts`. A fixed, exact expiry is needed because the
   verify page shows a live countdown to it.
2. Identity's built-in 6-digit email token. No extra columns, but its validity window is fuzzy (roughly 3 to 9
   minutes), so an honest countdown is impossible. Rejected after the countdown requirement.
3. Hold the registration as "pending" and only create the account after the code. Avoids half-made accounts but has to
   store the password hash somewhere temporary and duplicates the create flow. Rejected.

## Design
- **Sending:** Gmail SMTP (`smtp.gmail.com:587`, STARTTLS) from `medicare58.runasp.net@gmail.com` with an App Password,
  via MailKit. Settings section `Email` (`Host`, `Port`, `UserName`, `Password`, `FromAddress`, `FromName`).
  The password lives only in `appsettings.Production.json` / user secrets, never in git.
  In Development with no password set, the code is written to the log instead of sent.
- **Sign-up (user flow, as requested):** patient fills the form → account is created with
  `RequiresEmailVerification = true` → patient is signed in at once and lands straight on `/Account/VerifyEmail` →
  the code is emailed to the address they typed. The page shows the 6 code boxes, the address it went to, and a live
  countdown to the code's expiry (10 minutes). When it hits zero the boxes disable and "Send a new code" is offered.
  Correct code → straight into the site (home page, welcome message). **No second sign-in.**
  If sending fails, the account still exists and the page offers "Resend".
- **Code:** 6 random digits (`RandomNumberGenerator`), stored only as a hash, expires 10 minutes after sending,
  single-use, a new send replaces the old code. 5 wrong entries kill the current code (must request a new one).
- **Email:** mockup in `email-otp-mockup/` (HTML + PNG). Table layout with inline styles for Gmail/Outlook; the logo is
  sent as a PNG (Gmail does not show SVG). Plain-text part included.
- **Guard:** the existing `MustChangePasswordMiddleware` also redirects a signed-in user whose
  `RequiresEmailVerification && !EmailConfirmed` to `/Account/VerifyEmail` (allowing that page, resend, change-email
  and logout).
- **Verify:** correct code → `EmailConfirmed = true`, clear the code fields, refresh the cookie, go home with a welcome
  message. Wrong code → clear message with attempts left, plus rate limiting.
- **Resend / change email:** 60-second cooldown between sends (enforced on the server from `EmailCodeSentAtUtc`) plus rate limiting per IP; change-email re-checks uniqueness and sends a
  new code.
- **Database:** one EF migration adding the column, plus an idempotent SQL script in `deploy/` for the live database
  (`IF NOT EXISTS … ALTER TABLE AspNetUsers ADD …`, each column nullable or with a default). No data changes.
- **Tests:** unit tests for the policy (who must verify), the sign-up flow sets the flag, and middleware redirects.

## Verify page and motion (prototype: `email-otp-mockup/verify-page.html`, frames: `verify-states.png`)
Layout reuses the sign-in split card. Heading "Check your email", the masked address in a pill with a "Change"
button, six digit boxes (3 + 3), a status line, then a strip with the expiry ring (same idea as the queue GraceRing:
blue, amber in the last minute, red at zero) and "Resend in 0:45". Typing, pasting and phone/email autofill all fill
the boxes; the sixth digit submits by itself.

- **Typing:** each box gives a tiny 160 ms settle on entry. Nothing louder; this happens six times.
- **Checking:** the boxes ripple gently while the server answers.
- **Wrong code:** boxes turn red with a soft red glow and the row shakes (damped, 460 ms). The message says how many
  tries are left. After a beat the digits drop out right to left and focus returns to the first box.
- **Right code (the one authored moment):** boxes light up in a neon glow in the **identity blue** (`--teal`, #1456a8 /
  #86b8fa in dark), ride a quick wave, then **gather into one point** at the centre. A small **pop**: a light flash,
  a shockwave ring, and a burst of tiny medical crosses and dots. A blue badge springs in and a check mark draws.
  The heading changes to "Email confirmed / Welcome to MediCare, <name>", the button reads "Opening MediCare…" and
  fills like a progress bar, then the home page opens. About 2.5 s in total. No second sign-in.
- **Expired:** boxes disable, ring is red, "Send a new code" becomes the action; the ring refills on resend.
- **Reduced motion:** all of the above becomes instant colour/state changes; no shake, no burst.

## Left to the builder
Exact copy, layout details and spacing inside the existing design tokens; MailKit wiring; rate-limit numbers.
