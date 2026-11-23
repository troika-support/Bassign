---
description: "Hangfire (SQL Server); reminders, escalations, digests."
---

Use **Agent mode**. Follow `.github/copilot-instructions.md`.

## Goal
Spec §3.C scheduling with Hangfire.

## Do
1. Add Hangfire with SQL Server storage; admin-only dashboard. Thin job classes call an Application handler; make every job idempotent.
2. Reminders (due − §3.C.2 offset; no-op if Done); overdue escalations to managers (§3.C.3, with cooldown); daily digest (§3.C.4) in the org's local timezone; quiet hours (§3.C.5).
3. Send via INotificationChannel (WhatsApp/email); use ISummaryGenerator for digests.

## Tests first
A reminder no-ops when already Done; escalation respects cooldown; digest runs in local time.

## Done when
Jobs scheduled; a reminder fires against a seeded task (use a TimeProvider fake in tests).
