# MediCare committee presentation: design

## Intent
A new English PowerPoint deck for the project evaluation committee. It must show a real problem, a solution with clear business goals, and solid engineering, in about 10–15 minutes.

## Goals
- Cover the whole project, with the business objective and all six KPIs given their own space.
- Use the app's identity (colours, fonts, logo, illustrations).
- Attention-holding motion that supports the story.
- Show every team member with a well-framed photo.

## Non-goals and anti-goals
- No live-app screenshots, no Arabic version, no changes to application code.
- Existing decks in `docs/presentation/` stay untouched.
- Failure looks like: half-empty slides, small text, motion that distracts, claims the repository does not support, or invented numbers (market size, "reduces waiting by X%").

## Approaches
1. Improve `Animated-v2`: fastest, but looks and moves much like it does now.
2. **New deck told as one patient's queue ticket (chosen)**: strongest story and motion, more work.
3. Option 2 plus real app screenshots: more proof, needs the app and demo database running. Not chosen.

## Story
Ticket `A-014` travels from booking to prescription. It moves between slides with Morph transitions and its status chip changes (Pending → Confirmed → Checked in → Called → In consultation → Completed). Example data on the ticket is illustrative.

| # | Slide | Content |
|---|---|---|
| 1 | Title | Logo, "The clinic, run as a queue", YAT Learning Center 2026 |
| 2 | The problem | Full waiting room, unknown waits, walk-in chaos, double booking, doctor leaving early, fragmented records |
| 3 | Business objective | Shorter waits and more patients served, without hurting care quality or confirmed appointments |
| 4 | Six KPIs | Wq, Ws, Lq, utilization ρ, no-show rate, walk-in throughput, each with a plain meaning |
| 5 | Problem → solution | Traditional clinic against MediCare |
| 6 | Four roles | Admin, Doctor, Receptionist, Patient |
| 7 | Modules | The eight business modules |
| 8 | Journey: booking | Pending → Confirmed, no double booking, cancel/reschedule rules |
| 9 | Journey: arrival and call | Check-in, walk-ins, scheduled first then FIFO, doctor approval |
| 10 | Grace period | 3 × expected consultation, No-Show rule, with a countdown ring |
| 11 | Doctor leaves early | Block, worklist, contact log, "clinic unavailable"; extensions |
| 12 | Consultation and records | Visit record, addenda, prescriptions, protected attachments |
| 13 | Live board | Now serving `A-014`; ticket numbers only |
| 14 | Queue analytics | λ, μ, ρ, M/G/1 reference, Little's Law |
| 15 | Architecture and stack | Clean Architecture layers, .NET 10 stack |
| 16 | Quality and security | Test count, authorization, lockout, rate limiting, file protection |
| 17 | Team | Five photos and names, supervisor |
| 18 | Thank you | Questions, live demo next |

## Identity
Navy `#0A1F44`, royal blue `#1456A8`, cyan `#2EC4C6`, amber `#F1A628`, surface `#EEF3FA`, ink `#0B1F3F`. Big Shoulders Display for ticket numbers and big figures, Readex Pro for text. Logo and illustrations from the app. Dark slides open, close and mark turning points.

## Motion
Morph between slides for the ticket; short entrance animations that play automatically after a click, in reading order; a pulse line on dark slides; a wheel animation on the grace ring. The presenter advances; nothing auto-advances.

## Team photos
Order as in the README: Nour Mohamed Mahmoud, Georgette Nagy, Rehab Ezzat, Mohamed Shaaban, Alaa Osama. Equal circular frames, head and shoulders, eyes on the same line, face about the same size in each. Rehab's photo is straightened. Light exposure matching only; no retouching of faces. Originals are not modified.

## Constraints
- Built on this machine through PowerPoint, with native editable shapes and text.
- The already open `Presentation1` is not touched or closed.
- Body text 16 pt or larger; 16:9.
- Output: `docs/presentation/MediCare-Committee.pptx`. Nothing is committed to git.
- Facts come from `README.md` and `docs/`. The test count is re-checked against the repository before it appears.

## Left to the builder
Exact layouts, timing values, font embedding, speaker notes (short, one per slide).

## Done
The deck opens in PowerPoint without repair prompts; every slide is exported and visually checked for overflow and overlap; animations and transitions are confirmed present and the slide show runs through.
