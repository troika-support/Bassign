# Bassign — Copilot build kit

Drop this into an empty repository and build the app with GitHub Copilot in Visual Studio 2026
(Agent mode), one prompt at a time. Every rule, screen, and prompt traces back to `docs/spec.md`
and `docs/design-system.md`.

The name is *basa* (Shona for "work") + *assign* — assigning work. Bassign is an **internal
task-assignment app for a single organization**, built for Zimbabwe and **deployed as a separate
install per client org (each with its own database)** — no multi-tenancy, no subscription billing.
Managers use the web app; field staff use WhatsApp (primary) and the mobile app (offline). Built to
**your** .NET conventions: Clean Architecture, **MVC Controllers + source-generated Mediator +
Repository/Unit-of-Work**, EF Core + **SQL Server**, Blazor Interactive Server + MudBlazor, MAUI
Blazor Hybrid, Microsoft.Extensions.AI, Serilog, **Windows Server / IIS**.

## What's in the kit

| Path | What it does |
|---|---|
| `.github/copilot-instructions.md` | Repo-wide rules Copilot auto-loads. Stack, conventions, non-negotiables. |
| `.github/instructions/*.instructions.md` | Targeted rules by file path (api, ui, tests, mobile). |
| `.github/prompts/NN-*.prompt.md` | 18 ordered build prompts (00–17), invoked with `/NN-slug`. |
| `docs/spec.md` | Product spec — the source of truth. Rules cited as §3.A.1 etc. |
| `docs/design-system.md` | Tokens (AAA, verified), components, screen-by-screen layouts. |
| `docs/whatsapp.md` | The WhatsApp integration protocol Copilot won't know reliably. |
| `docs/decisions.md` | Resolved settings (providers, timings) — recommended defaults. |
| `docs/PROMPTS.md` | Index: command, what it builds, what you check, time. |
| `docs/BUILD-PROMPTS.md` | Every prompt in one copy-paste file, with a time plan. |
| `assets/theme-kit/` | Custom MudTheme, token + motion CSS, signature components, placeholder logo. |

## Setup (once)

1. Copy the whole kit into your empty repo root (so `.github/`, `docs/`, `assets/` sit at the top).
   Commit.
2. In VS 2026: **Tools > Options > GitHub > Copilot > Copilot Chat → enable custom instructions**.
   (Same paths work in VS Code.)
3. Install prerequisites: .NET 10 SDK, MAUI workload (`dotnet workload install maui`), Docker
   (for Testcontainers), SQL Server LocalDB.
4. Open the folder, open Copilot Chat in **Agent mode**, and run **`/00-plan`**. Then work
   `/01` → `/17` in order.

## Good habits

- **One prompt per chat.** Start a fresh Copilot chat for each prompt; build, run the prompt's
  "You check", and **commit** before the next.
- When Copilot drifts, point it at the section it missed ("follow design-system §8.4", "that
  violates non-negotiable rule 2").
- Don't let it invent package versions (use the matrix below) or business values (unknowns are
  `[set by CLIENT]` in the spec; most are resolved in `docs/decisions.md`).
- **Licences to confirm** before launch: SQL Server (Azure SQL or licence), Hangfire edition,
  WhatsApp BSP pricing, LLM provider billing (spec §8).

## Version matrix (verified October 2026 — pin these; don't invent)

| Package | Version | Notes |
|---|---|---|
| .NET SDK / C# | 10.0.x / 14 | |
| Microsoft.EntityFrameworkCore(.Design) | 10.0.9 | |
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.9 | moves in lockstep with EF Core |
| MudBlazor | 9.9.0 | Interactive Server (no static rendering) |
| Mediator.SourceGenerator / Mediator.Abstractions | 3.x *verify* | martinothamar, MIT (not MediatR) |
| Hangfire.AspNetCore / Hangfire.SqlServer | 1.8.23 | |
| Microsoft.Extensions.AI | 9.x *verify* | IChatClient / IEmbeddingGenerator |
| FluentValidation | 12.x *verify* | as a Mediator pipeline behaviour |
| Asp.Versioning.Mvc(.ApiExplorer) | 8.x *verify* | controllers |
| Scalar.AspNetCore | 2.16.x | |
| Serilog.AspNetCore | 9.x *verify* | |
| sqlite-net-pcl / SQLitePCLRaw.bundle_green | 1.9.x / 2.1.x *verify* | mobile, on-device |
| Microsoft.Maui.Controls / …Components.WebView.Maui | 10.0.x *verify* | with the workload |
| xunit.v3 / Testcontainers.MsSql | 3.x / 4.x *verify* | real SQL Server in tests |

Run `dotnet list package --outdated` after the first restore to refresh the *verify* rows.

## A note on scope

This kit gets you a well-architected, buildable app with an attentive developer driving Copilot and
reviewing each step. It is not autopilot: you answer any open questions, confirm external-API
signatures against current docs, and set up the WhatsApp / AI / IIS accounts. The kit is the
steering; you're the engine.
