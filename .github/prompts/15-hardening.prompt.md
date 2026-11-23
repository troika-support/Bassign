---
description: "Data Protection, security headers, rate limits, health checks, ZW data-protection, AAA pass. Checklist."
---

Use **Agent mode**. Follow `.github/copilot-instructions.md`.

## Goal
Production-safety pass as pass/fail checks.

## Do
1. [ ] Secrets encrypted where needed via Data Protection; none in source; ValidateOnStart green.
2. [ ] Strict JWT validation; locked-down CORS; security headers; rate limiting on auth + webhooks; admin-only Hangfire dashboard.
3. [ ] Zimbabwe Data Protection Act (spec §7): consent register, data-subject rights (access/rectify/erase/export), retention + hard-delete jobs; no PII in logs.
4. [ ] Accessibility AAA pass: run the contrast check; verify status is never colour-alone; prefers-reduced-motion disables animations.
5. [ ] Health-check endpoints; Serilog correlation ids.

## Done when
Every checkbox passes; a security + accessibility review is clean.
