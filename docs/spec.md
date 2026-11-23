# Bassign — Product Spec

Source of truth for *what* the product does. The Copilot instructions, design system, and
prompts all cite this file by section number. Business values are resolved in
`docs/decisions.md` (recommended starting points — confirm before launch); anything still open is
a `[set by CLIENT]` placeholder — never invent one.

Bassign is an internal task-assignment and tracking app for a single organization, built for the
Zimbabwean market. The name blends *basa* (Shona for "work") with *assign*. It is **deployed as a
separate install per client organization, each with its own database** — there is no multi-tenancy
and no subscription billing in the application itself.

---

## 1. Purpose and users

**1.1 Purpose.** Let managers assign work and see accountability at a glance, and let field
staff receive and complete tasks with the least possible friction, on poor connectivity and
budget devices.

**1.2 Deployment model.** **One install per organization**, each with its own database and its own
configuration (brand, WhatsApp number, AI key). The code is single-organization — no tenant
concept, no tenant isolation, no cross-organization data.

**1.3 Roles:**
- **Admin** — the first user set up for the install; full administration (team, settings, branding).
- **Manager** — assigns tasks, sees team workload and analytics, manages staff.
- **Staff** — receives, accepts, completes their own tasks; sees only their own work.

**1.4 Primary surfaces by audience.**
- Managers → **web app** (desktop), dense and oversight-focused.
- Field staff → **WhatsApp** (primary, zero-install) and the **mobile app** (opt-in, for offline
  capture and photo proof).

**1.5 Out of scope (v1):** payroll, shift scheduling/rostering, time-and-attendance, inventory,
customer-facing portals, subscription billing/payments, multi-tenancy, and task
dependencies/Gantt planning.

---

## 2. External systems

- **WhatsApp Business Platform** — inbound/outbound messages for field staff. Provider:
  **360dialog** (swappable behind a port). See `docs/whatsapp.md`.
- **LLM provider** — Claude (Anthropic) or Azure OpenAI, accessed through
  `Microsoft.Extensions.AI`. Provider is a config detail. See spec §3.F.
- **SMTP / email** — transactional email (invites, digests) via **SendGrid** (Amazon SES a
  lower-cost alternative).

---

## 3. Business rules

Each rule is a testable statement. Defaults are shown; admin-configurable ones say so.

**A. Tasks & assignment**
- A.1 A task has exactly one assignee or is *Unassigned*. Only Admin/Manager may create or assign.
- A.2 Valid status flow: `Todo → Accepted → Done`, or `Todo → Declined`. Any other transition is
  rejected. A `Done` or `Declined` task cannot be reassigned.
- A.3 `Complete` is idempotent — completing an already-done task changes nothing and is not an error.
- A.4 A task may require a **proof-of-work photo** on completion. Default: **not required**;
  configurable per install and per task.
- A.5 Due date, if set, must be in the future at creation time.

**B. Authorization**
- B.1 Every action is checked against the user's role via policy-based authorization
  (Admin/Manager/Staff). Staff may act only on their own tasks.
- B.2 Manager/Admin actions (create, assign, analytics, team, settings) are denied to Staff.

**C. Notifications & reminders** (all admin-configurable)
- C.1 On assignment, the assignee is notified immediately (WhatsApp if enrolled, else email).
- C.2 A **reminder** fires at `due − [default 2h]` if the task is not yet Done; it no-ops if Done.
- C.3 An **escalation** to the assignee's manager fires when a task is overdue by
  `[default 1h]`, then at most once per `[default 24h]` (cooldown) until resolved.
- C.4 A **daily digest** is sent to each manager at `[default 06:00]` in the org's local timezone.
- C.5 Quiet hours: no proactive messages between `[default 20:00–06:00]` local, except
  **escalations, which are allowed** (managers only); routine reminders to staff are suppressed.

**D. Accountability analytics** (manager-facing only)
- D.1 Metrics: on-time completion %, average time-to-done, completed count, per-person
  completion rate, workload (active/overdue counts), bottleneck detection.
- D.2 Staff see **only their own** work and metrics — no staff-visible ranking of peers.

**E. (reserved)** — billing/payments are out of scope (§1.5).

**F. AI features**
- F.1 Auto-assignment **suggests** an assignee (workload + skills); a human confirms. A
  suggestion is applied only if confidence ≥ `[default 0.6]` **and** the user exists.
- F.2 On AI timeout/error/unparseable output, the system degrades to a safe default (leave
  unassigned, "summary unavailable") and never blocks the core flow.
- F.3 AI input is capped and `max_tokens` is set on every call; token usage is metered.
- F.4 End-user text (task text, messages) is treated as untrusted (prompt-injection aware).

**G. Onboarding**
- G.1 First-run: the first user set up for the install becomes Admin and configures the
  organization profile (name, logo, brand accent).
- G.2 Staff join only via an **invite** whose token carries the role; invites expire after
  `[default 7 days]`.
- G.3 A WhatsApp number is bound to a user only after a **verified** one-time code; unknown
  numbers get an enrollment prompt, never task data.

---

## 4. Reports and notifications

- **Weekly accountability digest** (manager): exportable (PDF/CSV) summary of §3.D metrics.
- **Daily digest** (manager): §3.C.4.
- **Per-task notifications**: assignment, reminder, escalation, completion confirmation.
- Delivery channels: WhatsApp (templates outside the 24-hour window — see `docs/whatsapp.md`),
  email fallback.

---

## 5. Screens

Full layouts in `design-system.md` §8. Routes and roles:

| # | Screen | Route | Admin | Manager | Staff | Surface |
|---|---|---|---|---|---|---|
| 5.1 | Sign in | `/sign-in` | ✓ | ✓ | ✓ | Web |
| 5.2 | First-run setup | `/setup` | ✓ | — | — | Web |
| 5.3 | Workload dashboard | `/` | ✓ | ✓ | — | Web |
| 5.4 | Task list | `/tasks` | ✓ | ✓ | — | Web |
| 5.5 | Task detail | `/tasks/{id}` | ✓ | ✓ | — | Web |
| 5.6 | Assign/reassign (dialog) | — | ✓ | ✓ | — | Web |
| 5.7 | Accountability analytics | `/analytics` | ✓ | ✓ | — | Web |
| 5.8 | Team & invites | `/team` | ✓ | ✓ | — | Web |
| 5.9 | Settings & branding | `/settings` | ✓ | — | — | Web |
| 5.10 | Field task view | `/` | — | — | ✓ | Mobile |
| 5.11 | Invite accept | `/invite/{token}` | — | — | ✓ | Mobile/Web |
| 5.12 | WhatsApp task flow | n/a (conversation) | — | — | ✓ | WhatsApp |

**5.13 Sign-in.** Web uses **cookie auth**; mobile/API uses **JWT + refresh**. External logins
offered: **Google, Microsoft** (not Facebook).

---

## 6. Data model

Entities with key fields; **unique indexes in bold**. Single database per install — no tenant
column anywhere.

- **ApplicationUser** (Identity): Id, **Email (unique)**, DisplayName, Role, IsActive.
- **TaskItem**: Id, Title, Detail, DueDate, Status, AssigneeId, CreatedById,
  RequiresProofPhoto, ProofPhotoRef, CreatedAt, CompletedAt.
- **RefreshToken**: **TokenHash (unique)**, UserId, ExpiresAt, RevokedAt, ReplacedByHash.
- **Invite**: **Token (unique)**, Email, Role, ExpiresAt, AcceptedAt.
- **WhatsAppBinding**: **PhoneE164 (unique)**, UserId, Verified.
- **OrganizationSettings** (single row): Name, LogoUrl, PrimaryColorHex, WhatsAppNumber,
  NotificationDefaults, QuietHours, TimeZone.
- **AuditEntry**: Id, ActorId, Action, EntityType, EntityId, UtcTimestamp, DataJson.
- **AiUsage**: Id, Feature, InputTokens, OutputTokens, UtcTimestamp.
- **ProcessedWebhookMessage**: **ProviderMessageId (unique)**, ReceivedAt (WhatsApp dedupe).

Mobile-local (SQLite, device): TaskRow, OutboxItem, SyncMeta (see `maui-offline` patterns).

---

## 7. Infrastructure and deployment

- **Runtime:** .NET 10, ASP.NET Core. **Hosting: Windows Server / IIS**, one install per client org.
- **Database:** Microsoft SQL Server, one database per install (Azure SQL or a licensed instance in
  prod; LocalDB in dev).
- **Background jobs:** Hangfire with SQL Server storage; dashboard admin-only.
- **Secrets:** `dotnet user-secrets` in dev; **IIS app-pool environment variables** in prod (set
  via a PowerShell script); `ValidateOnStart` fail-fast. Sensitive values (e.g. the WhatsApp token)
  encrypted at rest via ASP.NET **Data Protection**. **MAUI apps hold no API keys** — all provider
  calls proxy through the API; device holds only auth tokens in SecureStorage.
- **Compliance:** Zimbabwe Cyber and Data Protection Act (Chapter 12:07) / SI 155 of 2024 —
  consent register, data-subject rights, PII encryption, breach notification, retention. Built as a
  baseline from v1; POTRAZ Data Controller licensing to be confirmed with a local legal advisor.

---

## 8. Licences to confirm

- **MudBlazor** — MIT (free). ✓
- **Mediator (source-generated, martinothamar)** — MIT (free). ✓ (Chosen over MediatR, which is
  commercially licensed v13+.)
- **Hangfire** — using the free OSS core (LGPL) + Hangfire.SqlServer.
- **WhatsApp Business Platform (360dialog)** — Meta per-conversation pricing + a 360dialog monthly
  fee (confirm current rates).
- **SQL Server** — Express/LocalDB in dev; SQL Server Standard on Windows Server or Azure SQL in prod.
- **LLM provider** — Anthropic Claude (Haiku-class) via Microsoft.Extensions.AI; usage billing
  (Azure OpenAI a drop-in alternative).
