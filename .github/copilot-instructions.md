# Copilot instructions — Bassign

Repository-wide rules for GitHub Copilot (Agent mode). Use the docs as the sources of truth and
**ask before deviating** from them:
- `docs/spec.md` — what the product does (rules are cited as §3.A.1 etc.).
- `docs/design-system.md` — UI tokens, components, screens.
- `docs/whatsapp.md` — the WhatsApp integration protocol.
- Targeted rules: `.github/instructions/*.instructions.md` (apply by file path).

Bassign is an **internal task-assignment app for a single organization**, built for the Zimbabwean
market and **deployed as a separate install per client org (each with its own database)**. No
multi-tenancy, no subscription billing. Managers use the web app; field staff use WhatsApp
(primary) and the mobile app (offline). Differentiators: AI assist, WhatsApp-native, accountability
analytics, offline reliability.

## Stack (fixed)

- **.NET 10 / C# 14.** Clean Architecture: `Domain` ← `Application` ← (`Infrastructure`, `Api`);
  `Web`, `Mobile`, `UI.Shared`.
- **API:** ASP.NET Core **MVC Controllers** (not minimal APIs).
- **Dispatch:** **source-generated Mediator** (martinothamar `Mediator`, MIT) for commands/queries.
- **Data:** EF Core + **SQL Server** (`Microsoft.EntityFrameworkCore.SqlServer`), accessed via
  **Repository + Unit of Work per aggregate** (not DbContext-as-UoW). One database per install.
- **Validation:** FluentValidation as a **Mediator pipeline behaviour**.
- **Errors:** Result pattern for expected failures; ProblemDetails (RFC 9457) at the boundary.
- **Auth:** ASP.NET Identity; **JWT** for API/mobile, **cookie** for Blazor web; role-based
  (Admin/Manager/Staff).
- **Web UI:** Blazor Web App **Interactive Server**, **MudBlazor** (custom MudTheme).
- **Mobile:** .NET MAUI **Blazor Hybrid** (Android-first), shared Razor Class Library, offline
  SQLite + outbox + sync.
- **Jobs:** Hangfire (SQL Server storage). **AI:** **Microsoft.Extensions.AI** (`IChatClient`,
  `IEmbeddingGenerator`) — provider is a DI/config detail. **Logging:** Serilog.
- **Messaging:** WhatsApp Business API (360dialog).
- **Hosting:** Windows Server / IIS, one install per client org. **Tests:** xUnit + Testcontainers
  (real SQL Server).

Versions are pinned in the kit README — use them; don't invent versions.

## Conventions

- Modern C#: primary constructors, collection expressions, records for DTOs, file-scoped
  namespaces, `TimeProvider` (never `DateTime.Now`), `CancellationToken` on all I/O.
- **Domain is pure:** business rules as methods on entities; no EF/ASP.NET.
- **Application** owns the ports (`INotificationChannel`, AI ports, repository interfaces) and the
  command/query handlers. **Infrastructure** implements the ports and the repositories.
- **Controllers are thin:** model-bind → send a command/query via Mediator → map Result to
  `IActionResult`/ProblemDetails. No business logic in controllers.
- **Blazor components get data through a service layer**, never a DbContext or repository directly.
- Repositories are **per aggregate**; a Unit of Work commits the transaction.

## Non-negotiable rules

1. **Authorization** (spec §3.B): every action is checked by role via policy-based authorization;
   Staff act only on their own tasks; manager/admin actions are denied to Staff. Enforce at the
   endpoint/handler, not just the UI.
2. **External systems behind Application ports** — EF and Mediator aside, the DB, LLM, WhatsApp, and
   email are interfaces; never call a provider SDK from Domain, a handler, or a component.
3. **Secrets never in source** (spec §7): user-secrets in dev, IIS app-pool env vars in prod,
   `ValidateOnStart`. Sensitive values encrypted at rest via Data Protection. **MAUI holds no API
   keys** — proxy through the API; device stores only tokens in SecureStorage.
4. **Offline is normal, not an error** (mobile): local SQLite is the UI's source of truth;
   optimistic UI + quiet "Pending" badge; never block the UI or show an error for a normal offline
   moment.
5. **WhatsApp 24-hour window** (whatsapp.md): free text only within 24h; otherwise an approved
   template. Verify webhook signatures; dedupe on message id.
6. **AI degrades gracefully** (spec §3.F): structured output validated (user exists + confidence
   threshold); on failure fall back to a safe default; cap input, set max_tokens, meter token usage.
7. **Accessibility AAA** (design-system §2/§4): text meets AAA; status is never conveyed by colour
   alone; `prefers-reduced-motion` reduces every animation to an instant state change.
8. **Audit** sensitive actions (assign, complete, invite, role change) to `AuditEntry`.
9. **Personal data** (spec §7): handle per Zimbabwe's Data Protection Act — consent, data-subject
   rights, retention; don't log PII.

## How to work

- Build in **small, buildable steps**; one prompt = one Copilot session. Build + commit between.
- **Tests first** for business rules, parsers, and the WhatsApp protocol — list the concrete cases
  before the code.
- Migrations are **reviewed scripts**, never auto-applied in production.
- When a value is missing or ambiguous, **list the questions** and wait — do not invent business
  values, versions, or API shapes. Unknown spec values are `[set by CLIENT]`.
