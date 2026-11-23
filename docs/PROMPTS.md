# Prompts index

Ordered build prompts. Invoke each in Copilot Chat (Agent mode) with `/` or `#prompt:`.
Build, run the checks, and commit after each. Full text in each file or in `BUILD-PROMPTS.md`.

| # | Command | Builds | You check | Time |
|---|---|---|---|---|
| 00 | `/00-plan` | Read the kit and reply with a build plan, questions, and package list. No code. | Answer any questions; correct any misread requirement before prompt 01. | 20 min |
| 01 | `/01-solution-setup` | Create the Clean Architecture solution, projects, central packages, build props, DI entry points. | Open the solution in VS 2026; confirm it builds and the project references are inward-only. | 30 min |
| 02 | `/02-domain` | TaskItem aggregate, enums, Result type, domain events. Pure, tests first. | Skim TaskItem — are all task rules here (not deferred to handlers)? | 40 min |
| 03 | `/03-persistence` | DbContext, EF configs with unique indexes, Repository + Unit of Work per aggregate, migration, seed. | Inspect the migration SQL; confirm the unique indexes exist. | 45 min |
| 04 | `/04-application-core` | Source-generated Mediator, validation behaviour, clock, audit, formatting, options. | Trace one command end to end in the debugger. | 40 min |
| 05 | `/05-identity-auth` | ASP.NET Identity, JWT (API/mobile) + cookie (web), roles, refresh tokens, invites. | Decode an issued JWT; confirm the role claims. | 55 min |
| 06 | `/06-api-controllers` | MVC controllers for the task workflow via Mediator, versioned, ProblemDetails; mobile sync endpoints. | Exercise create→assign→complete in Scalar; confirm ProblemDetails on a bad request. | 50 min |
| 07 | `/07-ui-shell` | Blazor Web App (Interactive Server), MudBlazor custom theme, layout, role nav, error pages. | Sign in as a manager; confirm nav, theme, and dark mode against design-system §2/§7. | 50 min |
| 08 | `/08-manager-screens` | Workload dashboard, task list, task detail, assign dialog (with AI chip stub). | Compare each screen to design-system §8; check the AAA tokens and status chips. | 90 min |
| 09 | `/09-analytics` | Manager analytics (on-time %, trend, per-person completion, bottleneck). Clarity, not surveillance. | Verify numbers against a seeded fixture; confirm no staff ranking is exposed. | 50 min |
| 10 | `/10-onboarding-team` | First-run admin setup, team & invites, settings & branding. | Walk the full setup → invite → accept flow. | 55 min |
| 11 | `/11-mobile-offline` | MAUI Blazor Hybrid, SQLite, outbox, sync, conflict merge, camera, secure tokens, field UI. | Airplane-mode test: complete a task offline, reconnect, confirm it syncs once. | 120 min |
| 12 | `/12-whatsapp` | INotificationChannel, interactive buttons, templates/24h window, signed webhook, binding. Riskiest integration. | Replay recorded payloads from whatsapp.md; confirm dedupe and the number→user mapping. | 100 min |
| 13 | `/13-ai` | Microsoft.Extensions.AI: auto-assign (structured + validated), summaries, embeddings/RAG seam, offline eval. | Force an AI error; confirm assignment still works (safe default). | 80 min |
| 14 | `/14-background-jobs` | Hangfire (SQL Server); reminders, escalations, digests. | Open the Hangfire dashboard (admin only); confirm scheduled jobs. | 55 min |
| 15 | `/15-hardening` | Data Protection, security headers, rate limits, health checks, ZW data-protection, AAA pass. Checklist. | Go through the checklist item by item before sign-off. | 60 min |
| 16 | `/16-integration-tests` | xUnit + Testcontainers (real SQL Server): main flows + the risky seams. | Run the suite locally; confirm the sync and WhatsApp tests are green. | 70 min |
| 17 | `/17-deploy-iis` | Publish profile, IIS config, PowerShell env-var script, per-client runbook, smoke test. | Run the smoke test against the deployed site; walk the go-live checklist. | 60 min |

**Order note:** single-tenant app (one install per client org). The riskiest external integrations are WhatsApp (12) and AI (13); they depend on the domain, API, and jobs, so they come after those. If you want a usable field experience sooner, 12 can be pulled forward once 06 is done.
