# MediCare Committee Deck Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. Execution is native (single session). The user approved the design and asked to start.

**Goal:** Build `docs/presentation/MediCare-Committee.pptx`, the 18-slide animated English deck.

**Architecture:** A photo-preparation script writes cropped portraits to the session scratchpad. A PowerPoint COM script builds the whole deck from scratch (rerunnable), saves it and exports slide PNGs for review. Application code is not touched.

**Tech Stack:** Windows PowerShell 5.1, System.Drawing, PowerPoint 16 COM.

**Spec:** `docs/superpowers/specs/2026-11-25-medicare-deck-design.md` (slide table, identity, motion and photo rules live there).

## Global Constraints
- 16:9, 960 x 540 pt. Body text 16 pt or larger.
- Colours `#0A1F44 #1456A8 #2EC4C6 #F1A628 #EEF3FA #0B1F3F`; Readex Pro for text, Big Shoulders Display for ticket numbers and big figures.
- Never close or edit `Presentation1`; never call `Application.Quit`.
- Existing decks and original photos are read only. Nothing is committed.
- No invented figures; facts from `README.md` and `docs/`.

## Review Focus
- Fonts missing on the presenting machine: embedded in the file.
- Text overflowing its box: every text shape is measured after layout.
- Morph unavailable or shape names unmatched: ticket shapes share `!!` names on every slide.
- A tilted or small portrait: no empty corners inside the circle.
- Presenting out of order or jumping back: each slide's animations are self-contained.

### Task 1: Assets
- [ ] Re-count tests from the repository (`dotnet test --list-tests`).
- [ ] Confirm both fonts are installed or install-free usable; extract logo and illustrations from the app/existing deck.
- [ ] `photos.ps1`: for each member `Crop(src, centerX, centerY, size, rotationDeg, brightness) -> <name>.png` (800 x 800). Check: contact sheet shows eyes on one line, similar face size, no empty corners.

### Task 2: Slides
- [ ] `build.ps1`: helpers `New-Slide(dark)`, `Add-Text`, `Add-Card`, `Add-Ticket(slide,x,y,scale,status)`; slides 1–18 per the spec table; one speaker note each.
- [ ] Check: 18 slides, exported PNGs reviewed, no overflow or overlap, no body text under 16 pt.

### Task 3: Motion
- [ ] Morph transition into ticket slides, fade elsewhere; auto-sequenced entrances after one click or on slide start; pulse line wipe; wheel on the grace ring; no timed advance.
- [ ] Check: per-slide effect count and transition printed from the saved file; no `AdvanceOnTime`.

### Task 4: Verify
- [ ] Reopen the saved file, run the slide show through all slides, confirm fonts embedded and `Presentation1` still open.
