# Tests

| Suite | What | Needs |
|---|---|---|
| `tests/Bassign.UnitTests` | Domain rules, handlers with fakes, prompts, scheduling maths, accessibility contrast, validators | nothing |
| `tests/Bassign.IntegrationTests` | The real API in-process (all middleware, auth, EF Core, background workers) against a real SQL Server | a SQL Server — see below |

## Integration tests — where the database comes from
1. `BASSIGN_TEST_SQL` (a connection string to a throwaway database), if set;
2. otherwise **Testcontainers** — a SQL Server 2022 container (Docker Desktop on Windows; the first run downloads ~1.5 GB);
3. otherwise, on Windows, **LocalDB** — a fresh `BassignTests_<guid>` database, dropped at the end.

The suite can never touch the dev database: settings come from environment variables set by the fixture, and it refuses to run if it finds itself on `Bassign`.

Covered: create → assign → accept → complete (+ outbox row, role and ownership checks, anonymous 401); sync delta pull, idempotent push, and server-wins conflict when a task is reassigned while the phone is offline; WhatsApp path token, HMAC signature, duplicate delivery processed once, worker reply to an unknown number; AI failure → assignment still works and the failure is metered, hallucinated candidates ignored; reminder fires once, escalation cooldown, no reminder for a finished task (controllable clock); health and security headers.

Run: `dotnet test tests/Bassign.IntegrationTests` (or Test Explorer). CI: `.github/workflows/ci.yml` runs both suites on every push/PR.
