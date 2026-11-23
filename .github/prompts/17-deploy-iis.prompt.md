---
description: "Publish profile, IIS config, PowerShell env-var script, per-client runbook, smoke test."
---

Use **Agent mode**. Follow `.github/copilot-instructions.md`.

## Goal
Ship to Windows Server / IIS per spec §7 — one install per client org.

## Do
1. Add a publish profile (framework-dependent or self-contained) and IIS config (app pool, ASP.NET Core Module).
2. PowerShell script to set app-pool environment variables (secrets) on the server; document ValidateOnStart behaviour.
3. Write a runbook: first-run per client (create the client's own database + run the migration script, create the first Admin), go-live checklist (DNS, HTTPS, WhatsApp number + approved templates, AI key), and a smoke-test script hitting health + sign-in + create-task.
4. Document the mobile app release (Android build/signing, Play listing) and WhatsApp template approval as prerequisites.

## Done when
A deploy to a test IIS server serves the app; smoke test passes.
