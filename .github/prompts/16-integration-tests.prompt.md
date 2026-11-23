---
description: "xUnit + Testcontainers (real SQL Server): main flows + the risky seams."
---

Use **Agent mode**. Follow `.github/copilot-instructions.md`.

## Goal
Confidence on real dependencies, per the tests instructions.

## Do
1. Set up WebApplicationFactory + Testcontainers (SQL Server). Seed an admin + users.
2. Cover: the create→assign→complete flow; sync push/pull/merge/conflict; WhatsApp signature+dedupe; the AI degrade path; reminder/escalation timing (TimeProvider).
3. Make the suite runnable in CI.

## Tests first
(this prompt is the tests)

## Done when
The full suite passes against containers.
