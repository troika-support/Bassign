# Bassign — build prompts (all in one)

Copy-paste version of every prompt. Work in order; **build, run the checks, commit** after each.
One prompt = one Copilot session (start a fresh chat each time). Use **Agent mode**.

## Before you start
1. Copy this kit into an empty repo. Put `.github/copilot-instructions.md` and `docs/` in place; commit.
2. In VS 2026: Tools > Options > GitHub > Copilot > Copilot Chat → enable custom instructions.
3. Install prerequisites: .NET 10 SDK, MAUI workload, Docker (Testcontainers), SQL Server LocalDB.
4. External accounts needed by later steps: WhatsApp Business number + approved templates (12), AI provider key (13).

## Contents
- [00 — Plan](#00-plan)
- [01 — Solution setup](#01-solution-setup)
- [02 — Domain model](#02-domain)
- [03 — Persistence — EF + repositories](#03-persistence)
- [04 — Application core](#04-application-core)
- [05 — Identity & auth](#05-identity-auth)
- [06 — API — task controllers + sync](#06-api-controllers)
- [07 — Web UI shell](#07-ui-shell)
- [08 — Manager screens](#08-manager-screens)
- [09 — Accountability analytics](#09-analytics)
- [10 — First-run setup, team, settings](#10-onboarding-team)
- [11 — Mobile app — offline](#11-mobile-offline)
- [12 — WhatsApp channel](#12-whatsapp)
- [13 — AI features](#13-ai)
- [14 — Background jobs](#14-background-jobs)
- [15 — Hardening](#15-hardening)
- [16 — Integration tests](#16-integration-tests)
- [17 — Deploy to IIS](#17-deploy-iis)


<a id="00-plan"></a>
### 00 — Plan
*Read the kit and reply with a build plan, questions, and package list. No code.*  ·  ~20 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
Understand the whole project before writing any code (spec, design system, integration docs, instructions).

#### Do
1. Read docs/spec.md, docs/design-system.md, docs/whatsapp.md, and all of .github/.
2. Reply with: a 5-line summary; the Clean Architecture solution structure you'll create; the entity list from spec §6; and the package list (from the README version matrix).
3. List any remaining [set by CLIENT] values (most are resolved in docs/decisions.md) as numbered questions.
4. Flag the riskiest integrations (WhatsApp §12, AI §13) and confirm the build order.

#### Done when
You've posted the summary, structure, entities, packages, and any questions. No code yet.
````

**You check:** Answer any questions; correct any misread requirement before prompt 01.

<a id="01-solution-setup"></a>
### 01 — Solution setup
*Create the Clean Architecture solution, projects, central packages, build props, DI entry points.*  ·  ~30 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
A building skeleton matching the stack in copilot-instructions (fixed) and the structure from prompt 00.

#### Do
1. Create a .slnx solution `Bassign` with projects: Domain, Application, Infrastructure, Api, Web, Mobile, UI.Shared under src/, and test projects under tests/.
2. References point inward only (Api/Web/Mobile → Application → Domain; Infrastructure → Application).
3. Add Directory.Packages.props (central package management) with the pinned versions from the README; Directory.Build.props (nullable, implicit usings, file-scoped namespaces, warnings-as-errors); .editorconfig for C# 14.
4. Wire Serilog and a DI composition root per project (AddApplication, AddInfrastructure, AddApi).
5. Add options types with ValidateOnStart for the settings groups (DB, Auth, WhatsApp, AI) — bound from config, no secrets in source.

#### Done when
`dotnet build` succeeds; all projects present; dependencies point inward; Serilog logs on startup.
````

**You check:** Open the solution in VS 2026; confirm it builds and the project references are inward-only.

<a id="02-domain"></a>
### 02 — Domain model
*TaskItem aggregate, enums, Result type, domain events. Pure, tests first.*  ·  ~40 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
The core aggregates with business rules as guarded methods (spec §3.A), no framework dependencies.

#### Do
1. Model TaskItem (spec §6) with Create/AssignTo/Accept/Complete/Decline as guarded methods enforcing the status flow (§3.A.2) and idempotent Complete (§3.A.3). Raise domain events.
2. Add enums (TaskStatus), the Result<T> + Error types, and value objects as needed. No EF, no ASP.NET.
3. Keep rules on the entity, not in future handlers.

#### Tests first
Write first: cannot reassign a Done/Declined task; Todo→Accepted→Done allowed, other transitions rejected; Complete twice is a no-op; Create rejects empty title and past due date (§3.A.5).

#### Done when
Domain tests pass; Domain project has no EF/ASP.NET reference.
````

**You check:** Skim TaskItem — are all task rules here (not deferred to handlers)?

<a id="03-persistence"></a>
### 03 — Persistence — EF + repositories
*DbContext, EF configs with unique indexes, Repository + Unit of Work per aggregate, migration, seed.*  ·  ~45 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
Data access via Repository + Unit of Work per aggregate (your convention), on SQL Server.

#### Do
1. Add AppDbContext (SQL Server) with IEntityTypeConfiguration<T> per entity and the unique indexes from spec §6 (bold ones).
2. Implement Repository + Unit of Work per aggregate (ITaskRepository, IUnitOfWork, etc.) in Infrastructure; interfaces in Application. Do not expose DbContext to handlers.
3. Add a design-time factory and the initial migration as a reviewed script. Add dev seed data (an admin user + a few sample tasks).
4. Connection string via user-secrets (dev); never appsettings. Don't auto-apply migrations in production.

#### Tests first
Repository round-trip; a unique-index violation surfaces as a handled conflict.

#### Done when
Migration applies to LocalDB; seed runs; a repository test passes.
````

**You check:** Inspect the migration SQL; confirm the unique indexes exist.

<a id="04-application-core"></a>
### 04 — Application core
*Source-generated Mediator, validation behaviour, clock, audit, formatting, options.*  ·  ~40 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
The application pipeline your conventions specify: Mediator + FluentValidation behaviour + cross-cutting.

#### Do
1. Add the source-generated Mediator (martinothamar Mediator, MIT). Set up command/query/handler conventions.
2. Add a FluentValidation pipeline behaviour (validate before handling; return a Result/ProblemDetails on failure).
3. Add an audit behaviour writing AuditEntry for sensitive actions (assign, complete, invite, role change); a TimeProvider-based clock; the fixed formatting helpers (design-system §5).
4. Confirm options + ValidateOnStart for all settings groups.

#### Tests first
Validation behaviour short-circuits an invalid command; audit behaviour records an entry.

#### Done when
Build passes; a command flows request→validation→handler→Result; an audit row is written.
````

**You check:** Trace one command end to end in the debugger.

<a id="05-identity-auth"></a>
### 05 — Identity & auth
*ASP.NET Identity, JWT (API/mobile) + cookie (web), roles, refresh tokens, invites.*  ·  ~55 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
Auth per spec §5.14: ASP.NET Identity, role-based, JWT for mobile and cookie for web.

#### Do
1. Add ASP.NET Identity with ApplicationUser (DisplayName, Role, IsActive). Configure JWT bearer (API/mobile) and cookie auth (Blazor web) over the same user store.
2. Issue an access token with role claims; add rotating, hashed, server-stored refresh tokens (rotate-on-use, reuse detection) and /auth/login + /auth/refresh.
3. Policy-based authorization (Admin, Managers policies). Google + Microsoft external logins (not Facebook).
4. Invite-based user onboarding (spec §3.G): the invite token carries the role + expiry; the first user to set up the app becomes Admin.

#### Tests first
Login issues a token with the right roles; refresh rotates + detects reuse; a non-manager is forbidden from a manager action.

#### Done when
Auth tests pass; you can sign in and hit a protected endpoint.
````

**You check:** Decode an issued JWT; confirm the role claims.

<a id="06-api-controllers"></a>
### 06 — API — task controllers + sync
*MVC controllers for the task workflow via Mediator, versioned, ProblemDetails; mobile sync endpoints.*  ·  ~50 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
The top-down workflow (spec §3.A) exposed as thin controllers, plus the mobile sync contract.

#### Do
1. Add commands/queries + handlers: CreateTask, AssignTask, AcceptTask, CompleteTask, DeclineTask, ListMyTasks, ListTeamWorkload. Thin controllers send them via Mediator and map Result → IActionResult/ProblemDetails.
2. Version the API (Asp.Versioning); wire Scalar for docs. Secure manager actions with the Managers policy.
3. Add sync endpoints: POST /api/v{n}/sync/push (idempotent — dedupe on outbox item id) and GET /sync/pull (delta token) for the mobile app.

#### Tests first
CreateTask persists; a non-manager is forbidden; push is idempotent (same item twice → one effect).

#### Done when
Endpoints respond in Scalar; workflow tests pass.
````

**You check:** Exercise create→assign→complete in Scalar; confirm ProblemDetails on a bad request.

<a id="07-ui-shell"></a>
### 07 — Web UI shell
*Blazor Web App (Interactive Server), MudBlazor custom theme, layout, role nav, error pages.*  ·  ~50 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
The manager web shell per design-system §7, themed from the organization's brand settings.

#### Do
1. Create the Blazor Web App (Global Interactive Server) with cookie auth. Install the theme kit from assets/theme-kit (MudTheme + tokens CSS).
2. Build the layout: MudDrawer role-based nav (design-system §7), MudAppBar with the org logo + user menu. Error pages.
3. Load the brand accent + logo from the organization settings (set once per install); map the accent to Palette.Primary. Validate the accent for contrast (design-system §2).
4. Add a typed service layer (components call services, not repositories/DbContext).
5. Wire the shared UI.Shared RCL.

#### Done when
App runs; nav reflects role; the org accent + logo show; dark mode works.
````

**You check:** Sign in as a manager; confirm nav, theme, and dark mode against design-system §2/§7.

<a id="08-manager-screens"></a>
### 08 — Manager screens
*Workload dashboard, task list, task detail, assign dialog (with AI chip stub).*  ·  ~90 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
The manager core per design-system §8.3–8.6 and spec §3.A/§3.D.

#### Do
1. Workload dashboard (§8.3): metric cards (Overdue/Due today/Unassigned/On-time) + per-person workload rows. The landing screen.
2. Task list (§8.4): MudDataGrid with search, status-filter pills, bulk actions, status chips.
3. Task detail (§8.5): meta, proof photo, activity timeline.
4. Assign/reassign dialog (§8.6): AI suggestion chip offered + overridable; stub the AI call for now (prompt 13 wires it).
5. Apply design-system voice, states, spacing; use services, not data access.

#### Done when
All four screens work against real data; empty/loading states render.
````

**You check:** Compare each screen to design-system §8; check the AAA tokens and status chips.

<a id="09-analytics"></a>
### 09 — Accountability analytics
*Manager analytics (on-time %, trend, per-person completion, bottleneck). Clarity, not surveillance.*  ·  ~50 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
Spec §3.D analytics on design-system §8.7 — no staff ranking.

#### Do
1. Add analytics queries projected to DTOs: on-time %, avg time-to-done, completed count, per-person completion, workload, bottleneck detection.
2. Build §8.7 with MudChart: metric cards, on-time-by-week bars, completion-by-person bars (low = warning colour, not shamed), bottleneck callout.
3. Add an exportable weekly digest (PDF/CSV). Staff-facing views show only their own work.
4. Add HybridCache for the heavier read models.

#### Tests first
On-time % matches a known fixture.

#### Done when
Analytics screen renders; export produces a file; staff can't see peers.
````

**You check:** Verify numbers against a seeded fixture; confirm no staff ranking is exposed.

<a id="10-onboarding-team"></a>
### 10 — First-run setup, team, settings
*First-run admin setup, team & invites, settings & branding.*  ·  ~55 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
First-run setup + team management (spec §3.G + §5.8–5.9).

#### Do
1. First-run setup (§8.2): the first user becomes Admin and sets the organization name, logo, and brand accent with a live preview.
2. Team & invites (§8.8): staff list, invite by email+role (token + expiry, §3.G.2), pending invites.
3. Settings & branding (§8.9): org name, logo, accent (contrast-validated), notification defaults (§3.C values), proof-photo default, quiet hours, timezone.

#### Tests first
Invite creates a token with the role; accepting it creates a user; an expired invite is rejected.

#### Done when
An admin can set up branding and invite a teammate who can accept and sign in.
````

**You check:** Walk the full setup → invite → accept flow.

<a id="11-mobile-offline"></a>
### 11 — Mobile app — offline
*MAUI Blazor Hybrid, SQLite, outbox, sync, conflict merge, camera, secure tokens, field UI.*  ·  ~120 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
The field app per the mobile instructions and design-system §8.11. Offline is normal.

#### Do
1. Generate the MAUI Blazor Hybrid app (Android-first) referencing UI.Shared. Point the BlazorWebView at a Main root.
2. Local SQLite (sqlite-net-pcl): TaskRow/OutboxItem/SyncMeta + LocalDb. Optimistic store (ITaskStore) that writes locally + enqueues an outbox item.
3. SyncService: idempotent push + delta pull against /sync/* (prompt 06); last-write-wins that never clobbers un-pushed dirty rows (push before pull). Sync on connectivity/resume/timer.
4. Camera service (MediaPicker + compress); SecureTokenStore (access + refresh in SecureStorage). Device holds NO API keys.
5. Field task view (§8.10): one task, big Accept/Decline/Done, quiet Pending badge, 'all caught up' empty state. A 401 on sync refreshes the token and retries once.

#### Tests first
Optimistic write enqueues one outbox item; push drains + dedupes; pull merges by token; a dirty local edit survives an older remote.

#### Done when
App runs on an emulator; works offline; syncs on reconnect; a photo attaches to completion.
````

**You check:** Airplane-mode test: complete a task offline, reconnect, confirm it syncs once.

<a id="12-whatsapp"></a>
### 12 — WhatsApp channel
*INotificationChannel, interactive buttons, templates/24h window, signed webhook, binding. Riskiest integration.*  ·  ~100 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
The primary field surface per docs/whatsapp.md. Follow that doc, not guesses.

#### Do
1. INotificationChannel (Application) triggered by domain events. WhatsAppChannel (Infrastructure) for 360dialog as a resilient typed HttpClient; options/secrets from config.
2. Outbound: interactive buttons (ids action:{taskId}); templates for reminders/digests with the 24-hour-window rule (whatsapp.md).
3. Inbound: signed webhook (verify signature; GET challenge), dedupe on message id, map the sender's number to a user, map to a command, ack fast, offload slow work to a job. Consume status callbacks.
4. Media (proof-of-work) download+compress+store. Phone→user binding with a verified one-time code; unknown numbers fail closed.

#### Tests first
Signature rejection; duplicate message id processed once; button id → correct command; outside-window send uses a template; unknown number → enrollment, no task data.

#### Done when
A simulated inbound Accept updates the task; an outbound assignment renders buttons.
````

**You check:** Replay recorded payloads from whatsapp.md; confirm dedupe and the number→user mapping.

<a id="13-ai"></a>
### 13 — AI features
*Microsoft.Extensions.AI: auto-assign (structured + validated), summaries, embeddings/RAG seam, offline eval.*  ·  ~80 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
Spec §3.F using Microsoft.Extensions.AI (your convention) — provider is a config detail.

#### Do
1. Add AI ports in Application (ITaskAssignmentAssistant, ISummaryGenerator). Implement with Microsoft.Extensions.AI IChatClient; provider (Claude/Azure OpenAI) via DI/config.
2. Auto-assign: request structured JSON {userId?, reasoning, confidence}; validate before acting (the user exists AND confidence ≥ §3.F.1 default 0.6); otherwise leave unassigned. Degrade on timeout/error/unparseable (§3.F.2).
3. Summaries (standup/digest) from structured task data; cap input; set max_tokens; meter token usage (AiUsage).
4. Add an IEmbeddingGenerator-based embeddings/RAG seam (task/skill matching) and a small offline evaluation harness for the assignment prompt (golden cases, scored).
5. Wire the suggestion into the assign dialog (replace the prompt-08 stub) and summaries into the digest job (prompt 14).

#### Tests first
Structured output parses; an unknown userId is ignored; malformed output degrades without throwing; offline eval runs and scores.

#### Done when
The assign dialog shows a real suggestion; a digest summary generates; eval harness passes its threshold.
````

**You check:** Force an AI error; confirm assignment still works (safe default).

<a id="14-background-jobs"></a>
### 14 — Background jobs
*Hangfire (SQL Server); reminders, escalations, digests.*  ·  ~55 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
Spec §3.C scheduling with Hangfire.

#### Do
1. Add Hangfire with SQL Server storage; admin-only dashboard. Thin job classes call an Application handler; make every job idempotent.
2. Reminders (due − §3.C.2 offset; no-op if Done); overdue escalations to managers (§3.C.3, with cooldown); daily digest (§3.C.4) in the org's local timezone; quiet hours (§3.C.5).
3. Send via INotificationChannel (WhatsApp/email); use ISummaryGenerator for digests.

#### Tests first
A reminder no-ops when already Done; escalation respects cooldown; digest runs in local time.

#### Done when
Jobs scheduled; a reminder fires against a seeded task (use a TimeProvider fake in tests).
````

**You check:** Open the Hangfire dashboard (admin only); confirm scheduled jobs.

<a id="15-hardening"></a>
### 15 — Hardening
*Data Protection, security headers, rate limits, health checks, ZW data-protection, AAA pass. Checklist.*  ·  ~60 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
Production-safety pass as pass/fail checks.

#### Do
1. [ ] Secrets encrypted where needed via Data Protection; none in source; ValidateOnStart green.
2. [ ] Strict JWT validation; locked-down CORS; security headers; rate limiting on auth + webhooks; admin-only Hangfire dashboard.
3. [ ] Zimbabwe Data Protection Act (spec §7): consent register, data-subject rights (access/rectify/erase/export), retention + hard-delete jobs; no PII in logs.
4. [ ] Accessibility AAA pass: run the contrast check; verify status is never colour-alone; prefers-reduced-motion disables animations.
5. [ ] Health-check endpoints; Serilog correlation ids.

#### Done when
Every checkbox passes; a security + accessibility review is clean.
````

**You check:** Go through the checklist item by item before sign-off.

<a id="16-integration-tests"></a>
### 16 — Integration tests
*xUnit + Testcontainers (real SQL Server): main flows + the risky seams.*  ·  ~70 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
Confidence on real dependencies, per the tests instructions.

#### Do
1. Set up WebApplicationFactory + Testcontainers (SQL Server). Seed an admin + users.
2. Cover: the create→assign→complete flow; sync push/pull/merge/conflict; WhatsApp signature+dedupe; the AI degrade path; reminder/escalation timing (TimeProvider).
3. Make the suite runnable in CI.

#### Tests first
(this prompt is the tests)

#### Done when
The full suite passes against containers.
````

**You check:** Run the suite locally; confirm the sync and WhatsApp tests are green.

<a id="17-deploy-iis"></a>
### 17 — Deploy to IIS
*Publish profile, IIS config, PowerShell env-var script, per-client runbook, smoke test.*  ·  ~60 min

**Paste this into Copilot Chat:**

````markdown
Use **Agent mode**. Follow `.github/copilot-instructions.md`.

#### Goal
Ship to Windows Server / IIS per spec §7 — one install per client org.

#### Do
1. Add a publish profile (framework-dependent or self-contained) and IIS config (app pool, ASP.NET Core Module).
2. PowerShell script to set app-pool environment variables (secrets) on the server; document ValidateOnStart behaviour.
3. Write a runbook: first-run per client (create the client's own database + run the migration script, create the first Admin), go-live checklist (DNS, HTTPS, WhatsApp number + approved templates, AI key), and a smoke-test script hitting health + sign-in + create-task.
4. Document the mobile app release (Android build/signing, Play listing) and WhatsApp template approval as prerequisites.

#### Done when
A deploy to a test IIS server serves the app; smoke test passes.
````

**You check:** Run the smoke test against the deployed site; walk the go-live checklist.

## Time plan

| # | Step | Time |
|---|---|---|
| 00 | Plan | 20 min |
| 01 | Solution setup | 30 min |
| 02 | Domain model | 40 min |
| 03 | Persistence — EF + repositories | 45 min |
| 04 | Application core | 40 min |
| 05 | Identity & auth | 55 min |
| 06 | API — task controllers + sync | 50 min |
| 07 | Web UI shell | 50 min |
| 08 | Manager screens | 90 min |
| 09 | Accountability analytics | 50 min |
| 10 | First-run setup, team, settings | 55 min |
| 11 | Mobile app — offline | 120 min |
| 12 | WhatsApp channel | 100 min |
| 13 | AI features | 80 min |
| 14 | Background jobs | 55 min |
| 15 | Hardening | 60 min |
| 16 | Integration tests | 70 min |
| 17 | Deploy to IIS | 60 min |
